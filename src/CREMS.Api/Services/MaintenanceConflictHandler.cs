using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Services;

public sealed class MaintenanceConflictHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken token)
    {
        var conflict = exception is MaintenanceConflictException or DbUpdateConcurrencyException || IsDeadlock(exception);
        if (!conflict) return false;
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(new { message = exception is MaintenanceConflictException
            ? exception.Message : "Another operation changed these records. Refresh and try again." }, token);
        return true;
    }

    public static bool IsDeadlock(Exception exception)
    {
        // EF can wrap a provider deadlock in DbUpdateException or InvalidOperationException.
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is SqlException { Number: 1205 }) return true;
        return false;
    }
}
