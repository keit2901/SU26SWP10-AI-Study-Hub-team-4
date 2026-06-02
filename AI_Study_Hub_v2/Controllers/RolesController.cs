using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI_Study_Hub_v2.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class RolesController : ControllerBase
{
    private readonly IRoleCatalogService _roles;

    public RolesController(IRoleCatalogService roles)
    {
        _roles = roles;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<RoleDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RoleDto>>> List(CancellationToken cancellationToken)
    {
        return Ok(await _roles.ListAsync(cancellationToken));
    }
}
