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

    [Test]
    public async Task GenerateAsync_SessionFolderMismatch_Throws404BeforeRagProviderQuotaOrPersistence()
    {
        using var db = TestDb.CreateInMemory();
        var user = SeedActiveStudent(db);
        var session = new ChatSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            FolderId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.ChatSessions.Add(session);
        await db.SaveChangesAsync();
        var rag = new Mock<IRagSearchService>(MockBehavior.Strict);
        var sut = CreateSut(db, rag.Object);

        var act = () => sut.GenerateAsync(user.SupabaseUserId, new GenerateQuizRequest(
            session.Id,
            FolderId: Guid.NewGuid()));

        var ex = await act.Should().ThrowAsync<QuizException>();
        ex.Which.StatusCode.Should().Be(404);
        ex.Which.Code.Should().Be("session_not_found");
        rag.VerifyNoOtherCalls();
        db.Quizzes.Should().BeEmpty();
    }

    [Test]
    public async Task GenerateAsync_SessionScopeChangesDuringProviderCall_Throws404WithoutPersistingQuizOrChatMessages()
    {
        using var db = TestDb.CreateInMemory();
        var user = SeedActiveStudent(db);
        var folderId = Guid.NewGuid();
        var session = new ChatSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            FolderId = folderId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.ChatSessions.Add(session);
        await db.SaveChangesAsync();

        var rag = new Mock<IRagSearchService>(MockBehavior.Strict);
        rag.Setup(s => s.SearchAsync(user.SupabaseUserId, It.IsAny<RagSearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new RagSearchResultDto("S1", Guid.NewGuid(), "source.pdf", 0, 1, "Grounded quiz source text.", 0.1),
            });
        var provider = new Mock<IAiChatCompletionClient>(MockBehavior.Strict);
        provider.Setup(p => p.CompleteAsync(It.IsAny<AiChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AiChatCompletionRequest, CancellationToken>((_, _) =>
            {
                session.FolderId = null;
                db.SaveChanges();
            })
            .ReturnsAsync(ValidQuizJson());
        var factory = new Mock<IAiChatCompletionClientFactory>(MockBehavior.Strict);
        factory.Setup(f => f.GetClient("test-model")).Returns(provider.Object);
        var quota = new Mock<IAiQuotaService>(MockBehavior.Strict);
        var reservation = new AiQuotaReservation(user.SupabaseUserId, 1024, DateOnly.FromDateTime(DateTime.UtcNow));
        quota.Setup(q => q.ReserveAsync(user.SupabaseUserId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(reservation);
        quota.Setup(q => q.CompleteAsync(reservation, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var persistence = new Mock<IChatPersistenceService>(MockBehavior.Strict);
        var sut = new QuizService(
            db,
            rag.Object,
            factory.Object,
            Microsoft.Extensions.Options.Options.Create(new GroqOptions { ApiKey = "test-key", Model = "test-model" }),
            Microsoft.Extensions.Options.Options.Create(new GeminiOptions { ApiKey = "test-key", Model = "configured-gemini" }),
            persistence.Object,
            quota.Object,
            Mock.Of<ILogger<QuizService>>());

        var act = () => sut.GenerateAsync(user.SupabaseUserId, new GenerateQuizRequest(
            session.Id,
            FolderId: folderId,
            Count: 3,
            Model: "test-model"));

        var ex = await act.Should().ThrowAsync<QuizException>();
        ex.Which.StatusCode.Should().Be(404);
        ex.Which.Code.Should().Be("session_not_found");
        db.Quizzes.Should().BeEmpty();
        db.ChatMessages.Should().BeEmpty();
        rag.VerifyAll();
        provider.VerifyAll();
        factory.VerifyAll();
        quota.VerifyAll();
        persistence.VerifyNoOtherCalls();
    }

    [Test]
    public async Task GenerateAsync_ProviderFailure_UsesConfiguredAlternateModel()
    {
        using var db = TestDb.CreateInMemory();
        var user = SeedActiveStudent(db);
        var folderId = Guid.NewGuid();
        var session = SeedSession(db, user);
        session.FolderId = folderId;
        await db.SaveChangesAsync();
        var rag = CreateRag(user.SupabaseUserId);
        var groq = new Mock<IAiChatCompletionClient>(MockBehavior.Strict);
        groq.Setup(client => client.CompleteAsync(It.IsAny<AiChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AiChatProviderException("groq_http_error", "unavailable"));
        var gemini = new Mock<IAiChatCompletionClient>(MockBehavior.Strict);
        gemini.Setup(client => client.CompleteAsync(It.IsAny<AiChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidQuizJson());
        var factory = new Mock<IAiChatCompletionClientFactory>(MockBehavior.Strict);
        factory.Setup(value => value.GetClient("configured-groq")).Returns(groq.Object);
        factory.Setup(value => value.GetClient("configured-gemini")).Returns(gemini.Object);
        var quota = new Mock<IAiQuotaService>(MockBehavior.Strict);
        var reservation = new AiQuotaReservation(user.SupabaseUserId, 1024, DateOnly.FromDateTime(DateTime.UtcNow));
        quota.Setup(value => value.ReserveAsync(user.SupabaseUserId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(reservation);
        quota.Setup(value => value.ReleaseAsync(reservation, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        quota.Setup(value => value.CompleteAsync(reservation, It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var sut = new QuizService(
            db,
            rag.Object,
            factory.Object,
            Microsoft.Extensions.Options.Options.Create(new GroqOptions { Model = "configured-groq" }),
            Microsoft.Extensions.Options.Options.Create(new GeminiOptions { Model = "configured-gemini" }),
            Mock.Of<IChatPersistenceService>(),
            quota.Object,
            Mock.Of<ILogger<QuizService>>());

        var quiz = await sut.GenerateAsync(user.SupabaseUserId, new GenerateQuizRequest(session.Id, FolderId: folderId, Count: 3));

        quiz.TotalQuestions.Should().Be(3);
        groq.Verify(client => client.CompleteAsync(It.IsAny<AiChatCompletionRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        gemini.Verify(client => client.CompleteAsync(
            It.Is<AiChatCompletionRequest>(request => request.ModelName == "configured-gemini"),
            It.IsAny<CancellationToken>()), Times.Once);
        factory.VerifyAll();
        quota.VerifyAll();
    }

    [Test]
    public async Task GetByIdAsync_RedactsUnansweredAnswerKeys_AndRevealsOnlySubmittedQuestion()
    {
        using var db = TestDb.CreateInMemory();
        var user = SeedActiveStudent(db);
        var quiz = SeedSprint2Quiz(db, user);
        var sut = CreateSut(db, Mock.Of<IRagSearchService>());

        var beforeAnswer = await sut.GetByIdAsync(user.SupabaseUserId, quiz.Id);

        beforeAnswer!.Questions.Select(q => q.CorrectOptionId).Should().OnlyContain(id => id == null);
        beforeAnswer.Questions.Select(q => q.Explanation).Should().OnlyContain(explanation => explanation == null);

        var saved = await sut.SaveAsync(user.SupabaseUserId, quiz.Id,
            new SaveQuizRequest(0, new Dictionary<int, string?> { [0] = "A" }));

        saved.Questions.Single(q => q.Index == 0).CorrectOptionId.Should().Be("A");
        saved.Questions.Single(q => q.Index == 0).Explanation.Should().Be("E1");
        saved.Questions.Where(q => q.Index != 0).Select(q => q.CorrectOptionId).Should().OnlyContain(id => id == null);
        saved.Questions.Where(q => q.Index != 0).Select(q => q.Explanation).Should().OnlyContain(explanation => explanation == null);
    }

    [Test]
    public async Task SaveAsync_GradesServerSide_MergesStaleAnswers_AndCompletesOnlyAfterAllQuestionsAnswered()
    {
        using var db = TestDb.CreateInMemory();
        var user = SeedActiveStudent(db);
        var quiz = SeedSprint2Quiz(db, user);
        var sut = CreateSut(db, Mock.Of<IRagSearchService>());

        var first = await sut.SaveAsync(user.SupabaseUserId, quiz.Id,
            new SaveQuizRequest(0, new Dictionary<int, string?> { [0] = "A" }));
        var second = await sut.SaveAsync(user.SupabaseUserId, quiz.Id,
            new SaveQuizRequest(2, new Dictionary<int, string?> { [2] = "C" }));

        first.Status.Should().Be(QuizStatus.InProgress);
        first.Score.Should().BeNull();
        second.Answers.Should().ContainKeys(0, 2);
        second.Submitted.Should().ContainKeys(0, 2);
        second.Status.Should().Be(QuizStatus.InProgress);
        second.Score.Should().BeNull();

        var completed = await sut.SaveAsync(user.SupabaseUserId, quiz.Id,
            new SaveQuizRequest(2, new Dictionary<int, string?> { [1] = "B" }));

        completed.Status.Should().Be(QuizStatus.Completed);
        completed.Score.Should().Be(3);
    }

    [Test]
    public async Task SaveAsync_RejectsInvalidQuestionIndexAndOption_AndDoesNotPersistEither()
    {
        using var db = TestDb.CreateInMemory();
        var user = SeedActiveStudent(db);
        var quiz = SeedSprint2Quiz(db, user);
        var sut = CreateSut(db, Mock.Of<IRagSearchService>());

        var invalidIndex = () => sut.SaveAsync(user.SupabaseUserId, quiz.Id,
            new SaveQuizRequest(0, new Dictionary<int, string?> { [3] = "A" }));
        var invalidOption = () => sut.SaveAsync(user.SupabaseUserId, quiz.Id,
            new SaveQuizRequest(0, new Dictionary<int, string?> { [0] = "Z" }));

        (await invalidIndex.Should().ThrowAsync<QuizException>()).Which.StatusCode.Should().Be(400);
        (await invalidOption.Should().ThrowAsync<QuizException>()).Which.StatusCode.Should().Be(400);
        (await db.Quizzes.AsNoTracking().SingleAsync(q => q.Id == quiz.Id)).AnswersJson.Should().Be("{}");
    }

    [Test]
    public async Task SaveAsync_AllowsSameAnswerRetry_ButRejectsChangingSubmittedAnswer()
    {
        using var db = TestDb.CreateInMemory();
        var user = SeedActiveStudent(db);
        var quiz = SeedSprint2Quiz(db, user);
        var sut = CreateSut(db, Mock.Of<IRagSearchService>());
        var answer = new SaveQuizRequest(0, new Dictionary<int, string?> { [0] = "A" });

        await sut.SaveAsync(user.SupabaseUserId, quiz.Id, answer);
        var retry = await sut.SaveAsync(user.SupabaseUserId, quiz.Id, answer);
        var change = () => sut.SaveAsync(user.SupabaseUserId, quiz.Id,
            new SaveQuizRequest(0, new Dictionary<int, string?> { [0] = "B" }));

        retry.Answers[0].Should().Be("A");
        var conflict = await change.Should().ThrowAsync<QuizException>();
        conflict.Which.StatusCode.Should().Be(409);
        conflict.Which.Code.Should().Be("answer_already_submitted");
    }

    [Test]
    public async Task SaveAsync_ForeignQuiz_Throws404()
    {
        using var db = TestDb.CreateInMemory();
        var owner = SeedActiveStudent(db);
        var other = SeedActiveStudent(db);
        var quiz = SeedSprint2Quiz(db, owner);
        var sut = CreateSut(db, Mock.Of<IRagSearchService>());

        var act = () => sut.SaveAsync(other.SupabaseUserId, quiz.Id,
            new SaveQuizRequest(0, new Dictionary<int, string?> { [0] = "A" }));

        var error = await act.Should().ThrowAsync<QuizException>();
        error.Which.StatusCode.Should().Be(404);
        error.Which.Code.Should().Be("quiz_not_found");
    }

    [Test]
    public async Task GenerateAsync_PersistsCanonicalScope_AndUsesOnlyFiveSameUserSameScopeQuizzes()
    {
        using var db = TestDb.CreateInMemory();
        var user = SeedActiveStudent(db);
        var other = SeedActiveStudent(db);
        var session = SeedSession(db, user);
        var folderId = Guid.NewGuid();
        session.FolderId = folderId;
        db.SaveChanges();
        var scope = CanonicalScope("folder", new[] { folderId }, "swp 391", "su26", "topic");
        for (var i = 0; i < 6; i++)
        {
            SeedPriorQuiz(db, user, scope, $"Same {i}", i);
        }
        SeedPriorQuiz(db, user, CanonicalScope("folder", new[] { Guid.NewGuid() }, "swp 391", "su26", "topic"), "Different scope", 10);
        SeedPriorQuiz(db, other, scope, "Other user", 10);
        SeedPriorQuiz(db, user, "{}", "Legacy", 10);
        var rag = CreateRag(user.SupabaseUserId);
        var (sut, requests) = CreateGenerationSut(db, rag.Object, ValidQuizJson());

        await sut.GenerateAsync(user.SupabaseUserId, new GenerateQuizRequest(
            session.Id, FolderId: folderId,
            SubjectCode: " SWP  391 ", Semester: " SU26 ", TopicKeyword: " topic ", Count: 3, Model: "test-model"));

        var generated = await db.Quizzes.OrderByDescending(q => q.CreatedAt).FirstAsync(q => q.SessionId == session.Id);
        generated.ScopeJson.Should().Be(scope);
        var prompt = requests.Single().SystemPrompt;
        prompt.Should().Contain("Same 0").And.Contain("Same 1").And.Contain("Same 2").And.Contain("Same 3").And.Contain("Same 4");
        prompt.Should().NotContain("Same 5").And.NotContain("Different scope").And.NotContain("Other user").And.NotContain("Legacy");
    }

    [Test]
    public async Task GenerateAsync_CapsExclusionPromptAtFortyQuestionsAndFourThousandCharacters()
    {
        using var db = TestDb.CreateInMemory();
        var user = SeedActiveStudent(db);
        var session = SeedSession(db, user);
        var folderId = Guid.NewGuid();
        session.FolderId = folderId;
        db.SaveChanges();
        var scope = CanonicalScope("folder", new[] { folderId }, null, null, null);
        for (var i = 0; i < 5; i++)
        {
            SeedPriorQuiz(db, user, scope, Enumerable.Range(0, 10).Select(question => $"Question {i}-{question} {new string('x', 120)}").ToArray(), i);
        }
        var rag = CreateRag(user.SupabaseUserId);
        var (sut, requests) = CreateGenerationSut(db, rag.Object, ValidQuizJson());

        await sut.GenerateAsync(user.SupabaseUserId, new GenerateQuizRequest(session.Id, FolderId: folderId, Count: 3, Model: "test-model"));

        var exclusion = requests.Single().SystemPrompt
            .Split("[EXCLUDED QUESTIONS — Do NOT generate these again]", StringSplitOptions.None)[1]
            .Split("Generate a quiz based ONLY", StringSplitOptions.None)[0];
        exclusion.Length.Should().BeLessThanOrEqualTo(4000);
        exclusion.Split("\n- ", StringSplitOptions.None).Length.Should().BeLessThanOrEqualTo(41);
        exclusion.Should().Contain("Question 0-0").And.NotContain("Question 4-9");
    }

    [Test]
    public async Task GenerateAsync_RepairsSameBatchDuplicate_AndDoesNotPersistRejectedOutput()
    {
        using var db = TestDb.CreateInMemory();
        var user = SeedActiveStudent(db);
        var session = SeedSession(db, user);
        var folderId = Guid.NewGuid();
        session.FolderId = folderId;
        db.SaveChanges();
        var rag = CreateRag(user.SupabaseUserId);
        var (sut, requests) = CreateGenerationSut(db, rag.Object, DuplicateQuizJson(), ValidQuizJson());

        await sut.GenerateAsync(user.SupabaseUserId, new GenerateQuizRequest(session.Id, FolderId: folderId, Count: 3, Model: "test-model"));

        requests.Should().HaveCount(2);
        requests[1].SystemPrompt.Should().Contain("duplicate question");
        db.Quizzes.Should().ContainSingle(q => q.SessionId == session.Id);
    }

    [Test]
    public async Task GenerateAsync_RepairsHistoricalDuplicate_AndBoundsTerminalInvalidCallsWithoutPersistence()
    {
        using var db = TestDb.CreateInMemory();
        var user = SeedActiveStudent(db);
        var session = SeedSession(db, user);
        var folderId = Guid.NewGuid();
        session.FolderId = folderId;
        db.SaveChanges();
        var scope = CanonicalScope("folder", new[] { folderId }, null, null, null);
        SeedPriorQuiz(db, user, scope, "Q1", 1);
        var rag = CreateRag(user.SupabaseUserId);
        var (sut, requests) = CreateGenerationSut(db, rag.Object, ValidQuizJson(), ValidQuizJson());

        var act = () => sut.GenerateAsync(user.SupabaseUserId,
            new GenerateQuizRequest(session.Id, FolderId: folderId, Count: 3, Model: "test-model"));

        var error = await act.Should().ThrowAsync<QuizException>();
        error.Which.Code.Should().Be("invalid_quiz_json");
        requests.Should().HaveCount(2);
        requests[1].SystemPrompt.Should().Contain("duplicate question");
        db.Quizzes.Should().ContainSingle(q => q.SessionId != session.Id);
    }

    [Test]
    public async Task GenerateAsync_RepairProviderFailure_UsesUnusedAlternateOnce_ThenRejectsInvalidContentWithoutPersistence()
    {
        using var db = TestDb.CreateInMemory();
        var user = SeedActiveStudent(db);
        var session = SeedSession(db, user);
        var folderId = Guid.NewGuid();
        session.FolderId = folderId;
        db.SaveChanges();
        var rag = CreateRag(user.SupabaseUserId);
        var primary = new Mock<IAiChatCompletionClient>(MockBehavior.Strict);
        primary.SetupSequence(client => client.CompleteAsync(It.IsAny<AiChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("not quiz json")
            .ThrowsAsync(new AiChatProviderException("primary_down", "unavailable"));
        var alternate = new Mock<IAiChatCompletionClient>(MockBehavior.Strict);
        alternate.Setup(client => client.CompleteAsync(It.IsAny<AiChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("still not quiz json");
        var factory = new Mock<IAiChatCompletionClientFactory>(MockBehavior.Strict);
        factory.Setup(value => value.GetClient("primary")).Returns(primary.Object);
        factory.Setup(value => value.GetClient("alternate")).Returns(alternate.Object);
        var quota = CreateQuota(user.SupabaseUserId);
        var persistence = new Mock<IChatPersistenceService>(MockBehavior.Strict);
        var sut = new QuizService(
            db, rag.Object, factory.Object,
            Microsoft.Extensions.Options.Options.Create(new GroqOptions { Model = "primary" }),
            Microsoft.Extensions.Options.Options.Create(new GeminiOptions { Model = "alternate" }),
            persistence.Object, quota.Object, Mock.Of<ILogger<QuizService>>());

        var act = () => sut.GenerateAsync(user.SupabaseUserId, new GenerateQuizRequest(session.Id, FolderId: folderId, Count: 3, Model: "primary"));

        var error = await act.Should().ThrowAsync<QuizException>();
        error.Which.Code.Should().Be("invalid_quiz_json");
        primary.Verify(client => client.CompleteAsync(It.IsAny<AiChatCompletionRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        alternate.Verify(client => client.CompleteAsync(It.IsAny<AiChatCompletionRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        db.Quizzes.Should().BeEmpty();
        persistence.VerifyNoOtherCalls();
    }

    [Test]
    public async Task GenerateAsync_CancellationDuringPrimaryCall_PropagatesWithoutFallbackPersistenceOrQuotaCompletion()
    {
        using var db = TestDb.CreateInMemory();
        using var cts = new CancellationTokenSource();
        var user = SeedActiveStudent(db);
        var session = SeedSession(db, user);
        var folderId = Guid.NewGuid();
        session.FolderId = folderId;
        db.SaveChanges();
        var rag = CreateRag(user.SupabaseUserId);
        var primary = new Mock<IAiChatCompletionClient>(MockBehavior.Strict);
        primary.Setup(client => client.CompleteAsync(It.IsAny<AiChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ThrowsAsync(new OperationCanceledException(cts.Token));
        var factory = new Mock<IAiChatCompletionClientFactory>(MockBehavior.Strict);
        factory.Setup(value => value.GetClient("primary")).Returns(primary.Object);
        var quota = CreateQuota(user.SupabaseUserId);
        var persistence = new Mock<IChatPersistenceService>(MockBehavior.Strict);
        var sut = new QuizService(
            db, rag.Object, factory.Object,
            Microsoft.Extensions.Options.Options.Create(new GroqOptions { Model = "primary" }),
            Microsoft.Extensions.Options.Options.Create(new GeminiOptions { Model = "alternate" }),
            persistence.Object, quota.Object, Mock.Of<ILogger<QuizService>>());

        var act = () => sut.GenerateAsync(user.SupabaseUserId, new GenerateQuizRequest(session.Id, FolderId: folderId, Count: 3, Model: "primary"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        primary.Verify(client => client.CompleteAsync(It.IsAny<AiChatCompletionRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        factory.Verify(value => value.GetClient("alternate"), Times.Never);
        quota.Verify(value => value.CompleteAsync(It.IsAny<AiQuotaReservation>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        db.Quizzes.Should().BeEmpty();
        persistence.VerifyNoOtherCalls();
    }

    private static string ValidQuizJson() =>
        """
        {
          "title": "Scoped quiz",
          "questions": [
            { "question": "Q1", "options": [{ "id": "A", "text": "A" }, { "id": "B", "text": "B" }, { "id": "C", "text": "C" }, { "id": "D", "text": "D" }], "correctOptionId": "A", "explanation": "E1" },
            { "question": "Q2", "options": [{ "id": "A", "text": "A" }, { "id": "B", "text": "B" }, { "id": "C", "text": "C" }, { "id": "D", "text": "D" }], "correctOptionId": "B", "explanation": "E2" },
            { "question": "Q3", "options": [{ "id": "A", "text": "A" }, { "id": "B", "text": "B" }, { "id": "C", "text": "C" }, { "id": "D", "text": "D" }], "correctOptionId": "C", "explanation": "E3" }
          ]
        }
        """;

    private static string DuplicateQuizJson() =>
        """
        {
          "title": "Duplicate quiz",
          "questions": [
            { "question": "Same question!", "options": [{ "id": "A", "text": "A" }, { "id": "B", "text": "B" }, { "id": "C", "text": "C" }, { "id": "D", "text": "D" }], "correctOptionId": "A", "explanation": "E1" },
            { "question": "  same   question ", "options": [{ "id": "A", "text": "A" }, { "id": "B", "text": "B" }, { "id": "C", "text": "C" }, { "id": "D", "text": "D" }], "correctOptionId": "B", "explanation": "E2" },
            { "question": "Q3", "options": [{ "id": "A", "text": "A" }, { "id": "B", "text": "B" }, { "id": "C", "text": "C" }, { "id": "D", "text": "D" }], "correctOptionId": "C", "explanation": "E3" }
          ]
        }
        """;

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
            Microsoft.Extensions.Options.Options.Create(new GeminiOptions()),
            Mock.Of<IChatPersistenceService>(),
            Mock.Of<IAiQuotaService>(),
            Mock.Of<ILogger<QuizService>>());
    }

    private static (QuizService Sut, List<AiChatCompletionRequest> Requests) CreateGenerationSut(
        AppDbContext db,
        IRagSearchService rag,
        params string[] responses)
    {
        var requests = new List<AiChatCompletionRequest>();
        var pendingResponses = new Queue<string>(responses);
        var provider = new Mock<IAiChatCompletionClient>(MockBehavior.Strict);
        provider.Setup(p => p.CompleteAsync(It.IsAny<AiChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AiChatCompletionRequest, CancellationToken>((request, _) => requests.Add(request))
            .ReturnsAsync(() => pendingResponses.Dequeue());
        var factory = new Mock<IAiChatCompletionClientFactory>(MockBehavior.Strict);
        factory.Setup(factory => factory.GetClient("test-model")).Returns(provider.Object);
        var quota = new Mock<IAiQuotaService>(MockBehavior.Strict);
        var reservation = new AiQuotaReservation(Guid.NewGuid(), 1024, DateOnly.FromDateTime(DateTime.UtcNow));
        quota.Setup(q => q.ReserveAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(reservation);
        quota.Setup(q => q.CompleteAsync(reservation, It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return (new QuizService(
            db,
            rag,
            factory.Object,
            Microsoft.Extensions.Options.Options.Create(new GroqOptions { ApiKey = "test-key", Model = "test-model" }),
            Microsoft.Extensions.Options.Options.Create(new GeminiOptions { ApiKey = "test-key", Model = "configured-gemini" }),
            Mock.Of<IChatPersistenceService>(),
            quota.Object,
            Mock.Of<ILogger<QuizService>>()), requests);
    }

    private static Mock<IRagSearchService> CreateRag(Guid supabaseUserId)
    {
        var rag = new Mock<IRagSearchService>(MockBehavior.Strict);
        rag.Setup(service => service.SearchAsync(supabaseUserId, It.IsAny<RagSearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new RagSearchResultDto("S1", Guid.NewGuid(), "source.pdf", 0, 1, "Quiz source context.", 0.1) });
        return rag;
    }

    private static Mock<IAiQuotaService> CreateQuota(Guid userId)
    {
        var reservation = new AiQuotaReservation(userId, 1024, DateOnly.FromDateTime(DateTime.UtcNow));
        var quota = new Mock<IAiQuotaService>(MockBehavior.Strict);
        quota.Setup(value => value.ReserveAsync(userId, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(reservation);
        quota.Setup(value => value.CompleteAsync(reservation, It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        quota.Setup(value => value.ReleaseAsync(reservation, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return quota;
    }

    private static ChatSession SeedSession(AppDbContext db, User user)
    {
        var session = new ChatSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.ChatSessions.Add(session);
        db.SaveChanges();
        return session;
    }

    private static void SeedDocuments(AppDbContext db, User user, IEnumerable<Guid> documentIds)
    {
        foreach (var id in documentIds)
        {
            db.Documents.Add(new Document
            {
                Id = id,
                UserId = user.Id,
                FileName = $"{id}.pdf",
                StoragePath = $"documents/{id}.pdf",
                MimeType = "application/pdf",
                SubjectCode = "SWP391",
                Semester = "SU26",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
        db.SaveChanges();
    }

    private static void SeedPriorQuiz(AppDbContext db, User user, string scope, string question, int age) =>
        SeedPriorQuiz(db, user, scope, new[] { question }, age);

    private static void SeedPriorQuiz(AppDbContext db, User user, string scope, string[] questions, int age)
    {
        db.Quizzes.Add(new Quiz
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            SessionId = Guid.NewGuid(),
            Status = QuizStatus.InProgress,
            ScopeJson = scope,
            QuestionsJson = System.Text.Json.JsonSerializer.Serialize(questions.Select((question, index) => new
            {
                index,
                question,
                subtitle = "",
                options = new[] { new { id = "A", text = "A" }, new { id = "B", text = "B" }, new { id = "C", text = "C" }, new { id = "D", text = "D" } },
                correctOptionId = "A",
                explanation = "E",
            })),
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-age),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-age),
        });
        db.SaveChanges();
    }

    private static string CanonicalScope(string kind, IEnumerable<Guid> ids, string? subjectCode, string? semester, string? topicKeyword) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            scopeKind = kind,
            scopeIds = ids.Select(id => id.ToString("D")).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            subjectCode,
            semester,
            topicKeyword,
        }, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });

    private static Quiz SeedSprint2Quiz(AppDbContext db, User user)
    {
        var questions = new[]
        {
            new { index = 0, question = "Q1", subtitle = "", options = new[] { new { id = "A", text = "A" }, new { id = "B", text = "B" } }, correctOptionId = "A", explanation = "E1", sourceLabel = "S1" },
            new { index = 1, question = "Q2", subtitle = "", options = new[] { new { id = "A", text = "A" }, new { id = "B", text = "B" } }, correctOptionId = "B", explanation = "E2", sourceLabel = "S1" },
            new { index = 2, question = "Q3", subtitle = "", options = new[] { new { id = "A", text = "A" }, new { id = "C", text = "C" } }, correctOptionId = "C", explanation = "E3", sourceLabel = "S1" },
        };
        var quiz = new Quiz
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            SessionId = Guid.NewGuid(),
            Title = "Sprint 2 quiz",
            Status = QuizStatus.InProgress,
            TotalQuestions = questions.Length,
            QuestionsJson = System.Text.Json.JsonSerializer.Serialize(questions),
            AnswersJson = "{}",
            SubmittedJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Quizzes.Add(quiz);
        db.SaveChanges();
        return quiz;
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
