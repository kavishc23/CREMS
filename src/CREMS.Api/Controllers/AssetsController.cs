using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/assets")]
[Authorize]
public sealed class AssetsController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<Asset>>> GetAll(CancellationToken cancellationToken)
    {
        var assets = await db.Assets.AsNoTracking().OrderBy(x => x.AssetNumber)
            .ToListAsync(cancellationToken);
        return Ok(assets);
    }
}

