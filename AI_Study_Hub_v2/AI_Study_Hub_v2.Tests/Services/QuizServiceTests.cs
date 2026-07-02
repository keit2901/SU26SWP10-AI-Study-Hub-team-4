using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Services.Rag;
using AI_Study_Hub_v2.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public sealed class QuizServiceTests
{
    [Test]
    public async Task GenerateAsync_UsesScopedRagFilters_PersistsQuiz_AndHidesCorrectAnswer()
    {
        using var db = TestDb.CreateInMemory();
        var user = SeedActiveStudent(db);
        var documentId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        RagSearchRequest? capturedRequest = null;
        Guid? capturedSupabaseUserId = null;

        var rag = new Mock<IRagSearchService>(MockBehavior.Strict);
        rag.Setup(s => s.SearchAsync(It.IsAny<Guid>(), It.IsAny<RagSearchRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, RagSearchRequest, CancellationToken>((uid, request, _) =>
            {
                capturedSupabaseUserId = uid;
                capturedRequest = request;
            })
            .ReturnsAsync(new[]
            {
                new RagSearchResultDto("S1", documentId, "rag.pdf", 0, 2, "Retrieval augmented generation grounds answers in indexed source excerpts.", 0.1),
                new RagSearchResultDto("S2", Guid.NewGuid(), "quiz.pdf", 1, 3, "Quiz generation should reuse retrieved context deterministically.", 0.2),
            });

        var sut = CreateSut(db, rag.Object);

        var response = await sut.GenerateAsyncV2(user.SupabaseUserId, new QuizGenerateRequestV2(
            Prompt: " rag quiz ",
            DocumentId: documentId,
            FolderId: folderId,
            DocumentIds: new[] { documentId },
            SubjectCode: " swp391 ",
            Semester: " su26 ",
            TopK: 4,
            QuestionCount: 2));

        response.QuizId.Should().NotBeEmpty();
        response.Title.Should().Be("Quiz: rag quiz");
        response.Questions.Should().HaveCount(2);
        response.Questions.Should().OnlyContain(q => q.Explanation == null);
        response.Questions[0].Options.Select(o => o.Id).Should().BeEquivalentTo(new[] { "A", "B", "C", "D" });
        response.Sources.Should().HaveCount(2);
        capturedSupabaseUserId.Should().Be(user.SupabaseUserId);
        capturedRequest.Should().NotBeNull();
        capturedRequest!.DocumentId.Should().Be(documentId);
        capturedRequest.FolderId.Should().Be(folderId);
        capturedRequest.DocumentIds.Should().ContainSingle().Which.Should().Be(documentId);
        capturedRequest.SubjectCode.Should().Be("SWP391");
        capturedRequest.Semester.Should().Be("SU26");
        capturedRequest.TopK.Should().Be(4);

        var row = await db.Quizzes.AsNoTracking().SingleAsync();
        row.UserId.Should().Be(user.Id);
        row.QuestionsJson.Should().Contain("correctOptionId");
        row.ScopeJson.Should().Contain("SWP391");
        rag.VerifyAll();
    }

    [Test]
    public async Task GenerateAsync_NoSources_Throws404_NoQuizSources()
    {
        using var db = TestDb.CreateInMemory();
        var user = SeedActiveStudent(db);
        var rag = new Mock<IRagSearchService>(MockBehavior.Strict);
        rag.Setup(s => s.SearchAsync(It.IsAny<Guid>(), It.IsAny<RagSearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RagSearchResultDto>());
        var sut = CreateSut(db, rag.Object);

        var act = () => sut.GenerateAsyncV2(user.SupabaseUserId, new QuizGenerateRequestV2("RAG"));

        var ex = await act.Should().ThrowAsync<AiStudyFeatureException>();
        ex.Which.StatusCode.Should().Be(404);
        ex.Which.Code.Should().Be("no_quiz_sources");
        db.Quizzes.Should().BeEmpty();
        rag.VerifyAll();
    }

    [Test]
    public async Task SubmitAsync_GradesAnswers_AndPersistsAttempt()
    {
        using var db = TestDb.CreateInMemory();
        var user = SeedActiveStudent(db);
        var rag = new Mock<IRagSearchService>(MockBehavior.Strict);
        rag.Setup(s => s.SearchAsync(It.IsAny<Guid>(), It.IsAny<RagSearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new RagSearchResultDto("S1", Guid.NewGuid(), "rag.pdf", 0, 1, "RAG retrieves relevant document chunks before answering.", 0.1),
            });
        var sut = CreateSut(db, rag.Object);
        var quiz = await sut.GenerateAsyncV2(user.SupabaseUserId, new QuizGenerateRequestV2("RAG retrieval", QuestionCount: 1));

        var response = await sut.SubmitAsync(user.SupabaseUserId, quiz.QuizId, new QuizSubmitRequest(new[]
        {
            new QuizAnswerDto("q1", "B"),
        }));

        response.Score.Should().Be(1);
        response.Total.Should().Be(1);
        response.Results.Should().ContainSingle().Which.CorrectOptionId.Should().Be("B");
        response.Results[0].Explanation.Should().Contain("S1");

        // Quiz is updated with score in the database
        var updatedQuiz = await db.Quizzes.AsNoTracking().SingleAsync(q => q.Id == quiz.QuizId);
        updatedQuiz.Score.Should().Be(1);
        rag.VerifyAll();
    }

    [Test]
    public async Task SubmitAsync_ForeignQuiz_Throws404()
    {
        using var db = TestDb.CreateInMemory();
        var owner = SeedActiveStudent(db);
        var other = SeedActiveStudent(db);
        var quiz = new Quiz
        {
            Id = Guid.NewGuid(),
            UserId = owner.Id,
            Title = "Foreign quiz",
            QuestionsJson = "[]",
            ScopeJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Quizzes.Add(quiz);
        await db.SaveChangesAsync();
        var sut = CreateSut(db, Mock.Of<IRagSearchService>());

        var act = () => sut.SubmitAsync(other.SupabaseUserId, quiz.Id, new QuizSubmitRequest(Array.Empty<QuizAnswerDto>()));

        var ex = await act.Should().ThrowAsync<AiStudyFeatureException>();
        ex.Which.StatusCode.Should().Be(404);
        ex.Which.Code.Should().Be("quiz_not_found");
    }

    private static QuizService CreateSut(AppDbContext db, IRagSearchService rag)
    {
        var groqOptions = Microsoft.Extensions.Options.Options.Create(new GroqOptions
        {
            ApiKey = "test-key",
            Model = "llama-3.3-70b-versatile",
        });
        return new QuizService(
            db,
            rag,
            Mock.Of<IAiChatCompletionClientFactory>(),
            groqOptions,
            Mock.Of<IChatPersistenceService>(),
            Mock.Of<IAiQuotaService>(),
            Mock.Of<ILogger<QuizService>>());
    }

    private static User SeedActiveStudent(AppDbContext db)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            RoleId = 2,
            SupabaseUserId = Guid.NewGuid(),
            Username = $"u{Guid.NewGuid():N}"[..10],
            FullName = "Test User",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }
}
