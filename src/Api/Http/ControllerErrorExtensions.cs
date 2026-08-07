using Microsoft.AspNetCore.Mvc;

namespace Keepr.Api.Http;

/// <summary>
/// The one place a user-facing error gets its stable machine <c>code</c> (#30). Builds the same
/// problem+json body <c>ControllerBase.Problem</c> would (via the controller's
/// <c>ProblemDetailsFactory</c>, so Type/Title/traceId are still filled in), then tags it with the
/// <c>code</c> the client localizes off. Replaces the ad-hoc
/// <c>new ProblemDetails { … }; pd.Extensions["code"] = …</c> that was scattered across controllers.
/// See docs/feature-30-localization.md §5.
/// </summary>
public static class ControllerErrorExtensions
{
    /// <param name="controller">The controller producing the response (the extension's receiver).</param>
    /// <param name="status">HTTP status code (also the ProblemDetails <c>status</c>).</param>
    /// <param name="code">A stable <see cref="ErrorCodes"/> value — the client's translation key.</param>
    /// <param name="detail">Human-readable English detail: the fallback when the client can't map the code.</param>
    public static ObjectResult CodedProblem(
        this ControllerBase controller, int status, string code, string detail)
    {
        var problem = controller.ProblemDetailsFactory.CreateProblemDetails(
            controller.HttpContext, statusCode: status, detail: detail);
        problem.Extensions["code"] = code;
        return new ObjectResult(problem) { StatusCode = status };
    }
}
