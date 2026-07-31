using System.Reflection;
using AI_Study_Hub_v2.Controllers;
using AI_Study_Hub_v2.Services;
using FluentAssertions.Execution;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public class DashboardModerationContractTests
{
    private static readonly string[] RequiredModerationServiceMethods =
    [
        "GetPendingModerationDocumentsAsync",
        "ApproveDocumentAsync",
        "RejectDocumentAsync",
        "GetModerationAnalyticsAsync",
        "GetModerationDocumentSignedUrlAsync"
    ];

    private static readonly string[] RequiredModerationControllerMethods =
    [
        "GetPendingModerationDocuments",
        "AiReviewDocument",
        "ApproveDocument",
        "RejectDocument"
    ];

    [Test]
    public void Dashboard_service_moderation_operations_must_be_caller_aware_and_cancellable()
    {
        var methods = typeof(IDashboardService).GetMethods(BindingFlags.Instance | BindingFlags.Public);

        using var scope = new AssertionScope();
        foreach (var methodName in RequiredModerationServiceMethods)
        {
            methods.Should().Contain(
                method => method.Name == methodName && HasCallerIdentityAndCancellationToken(method),
                $"{methodName} must receive the authenticated supabaseUserId and a CancellationToken so a Student cannot bypass moderation authorization through the service boundary");
        }

        methods.Where(method => method.Name is "ApproveDocumentAsync" or "RejectDocumentAsync")
            .Should().OnlyContain(
                method => HasCallerIdentityAndCancellationToken(method),
                "caller-less public approve/reject overloads let a Student bypass the moderation authorization boundary");
    }

    [Test]
    public void Dashboard_controller_moderation_actions_must_be_explicitly_limited_to_admins_and_moderators()
    {
        var methods = typeof(DashboardController).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        using var scope = new AssertionScope();
        foreach (var methodName in RequiredModerationControllerMethods)
        {
            methods.Should().Contain(
                method => method.Name == methodName,
                $"DashboardController must expose a {methodName} moderation action; without it, a Student-facing path can bypass the intended moderation boundary");

            var method = methods.SingleOrDefault(candidate => candidate.Name == methodName);
            if (method is not null)
            {
                method.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Should().BeEmpty(
                    $"{methodName} must not allow anonymous access because anonymous or Student callers must not bypass moderation");

                method.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Should().Contain(
                    attribute => HasAdminAndModeratorRoles(attribute),
                    $"{methodName} must explicitly require both Admin and Moderator roles so a Student cannot invoke moderation actions");
            }
        }
    }

    [Test]
    public void Ai_review_must_not_be_a_public_callerless_service_operation()
    {
        var aiReviewMethods = typeof(IDashboardService)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.Name == "AiReviewDocumentAsync")
            .ToArray();

        aiReviewMethods.Should().OnlyContain(
            method => HasCallerIdentityAndCancellationToken(method),
            "a public caller-less AiReviewDocumentAsync lets a Student bypass the moderation authorization boundary; make it caller-aware and cancellable or remove it from the public interface");
    }

    private static bool HasCallerIdentityAndCancellationToken(MethodInfo method)
    {
        var parameters = method.GetParameters();
        return parameters.Any(parameter => parameter.ParameterType == typeof(Guid) && parameter.Name == "supabaseUserId")
            && parameters.Any(parameter => parameter.ParameterType == typeof(CancellationToken));
    }

    private static bool HasAdminAndModeratorRoles(AuthorizeAttribute attribute)
    {
        var roles = (attribute.Roles ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return roles.Contains("Admin", StringComparer.Ordinal)
            && roles.Contains("Moderator", StringComparer.Ordinal);
    }
}
