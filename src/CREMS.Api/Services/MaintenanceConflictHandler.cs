using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Services;

public sealed class MaintenanceConflictHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken token)
    {
        if (exception is DbUpdateException)
        {
            for (Exception? current = exception; current is not null; current = current.InnerException)
            {
                if (current is not SqlException sql) continue;
                var message = sql.Number switch
                {
                    2601 or 2627 => "A record with the same unique reference already exists. Refresh and use a different reference.",
                    547 => "A linked record is missing or still in use. Review the selected references before saving or deleting.",
                    2628 or 8152 => "One or more text values exceed the supported field length. Shorten the values and try again.",
                    _ => null
                };
                if (message is null) continue;
                context.Response.StatusCode = sql.Number is 2601 or 2627 or 547 ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { message }, token);
                return true;
            }
        }
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
