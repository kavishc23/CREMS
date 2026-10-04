using CREMS.Api.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using System.Text.Json.Nodes;

namespace CREMS.Api.Services;

public static class ApiDocumentation
{
    public static void Configure(OpenApiOptions options)
    {
        options.AddDocumentTransformer((document, context, token) =>
        {
            document.Info.Title = "CREMS API";
            document.Info.Version = "v1";
            document.Info.Description = """
                Explore endpoints using the filter below. Expand a group, choose an endpoint, then **Try it out → Execute**.

                ### Sign in to protected endpoints
                Use the **API session** panel above to sign in, complete MFA, refresh session details, or sign out.
                No bearer token or API key is required. You can also execute **POST /api/auth/login** with your email and password.
                If the response is 202 with `requiresMfa`, complete **POST /api/auth/mfa/verify**.
                The browser stores the HttpOnly session cookie; Swagger automatically sends the browser-window header.
                Use **GET /api/auth/session** to check a staff session. Customer accounts use **GET /api/customer-account/session**.

                **Lock icons describe authentication requirements, not your live login status.**
                The Authorize dialog documents cookie names; do not paste a token or cookie there.
                Sign in through the login endpoint instead. To end a staff session, execute **POST /api/auth/logout**;
                customers use **POST /api/customer-account/logout**.

                **401** means login is missing, expired, or belongs to another browser window. Sign in again in this Swagger tab.
                **403** means your account lacks the required role, permission, or branch/division access.
                Logging in does not bypass those checks. Sessions have a 15-minute limit.

                Swagger is available only in Development. Executing write endpoints changes the connected database.
                """;
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            foreach (var (id, name) in new[] { ("StaffSession", "CREMS.Session"), ("CustomerSession", "CREMS.CustomerSession") })
                document.Components.SecuritySchemes[id] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Cookie,
                    Name = name,
                    Description = "HttpOnly cookie set by POST /api/auth/login. No manual token entry. The browser sends it automatically after login; this dialog does not sign you in or out."
                };
            return Task.CompletedTask;
        });
        options.AddOperationTransformer(async (operation, context, token) =>
        {
            AddExample(operation, context.Description.RelativePath, context.Description.HttpMethod);
            var metadata = context.Description.ActionDescriptor.EndpointMetadata;
            var rules = metadata.OfType<IAuthorizeData>().ToArray();
            if (metadata.OfType<IAllowAnonymous>().Any() || rules.Length == 0)
            {
                operation.Security = [];
                return;
            }
            var customer = rules.Any(rule => rule.Policy == SystemPolicies.CustomerPortal);
            operation.Security = [new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(customer ? "CustomerSession" : "StaffSession", context.Document)] = []
            }];
            var policies = rules.Select(rule => rule.Policy).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct();
            var provider = context.ApplicationServices.GetRequiredService<IAuthorizationPolicyProvider>();
            var resolved = await AuthorizationPolicy.CombineAsync(provider, rules);
            var roleGroups = resolved?.Requirements.OfType<RolesAuthorizationRequirement>()
                .Select(requirement => "(" + string.Join(" OR ", requirement.AllowedRoles) + ")").ToArray() ?? [];
            var permissionNames = resolved?.Requirements.OfType<PermissionRequirement>()
                .Select(requirement => requirement.Permission).ToArray() ?? [];
            operation.Description = $"{operation.Description}\n\nRequires a {(customer ? "customer" : "staff")} login session. " +
                $"Policies: {string.Join(", ", policies.DefaultIfEmpty("Authenticated user"))}. " +
                (roleGroups.Length > 0 ? $"Required roles: {string.Join(" AND ", roleGroups)}. " : "") +
                (permissionNames.Length > 0 ? $"Required permissions: {string.Join(", ", permissionNames)}. Permission grants and overrides are configured per account/role. " : "") +
                "Branch, division and resource access checks still apply.";
            operation.Responses ??= new OpenApiResponses();
            operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Sign in again: missing, expired or invalid browser-window session." });
            operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Your account does not have the required role, permission or resource access." });
        });
    }

    private static void AddExample(OpenApiOperation operation, string? path, string? method)
    {
        var tomorrow = DateTimeOffset.UtcNow.Date.AddDays(1);
        object? example = (path, method) switch
        {
            ("api/customers", "POST") => new { customerNumber = "CUS-900001", name = "Demo Customer", email = "demo.customer@example.invalid", phone = "+679 9990000", address = "Demo job site, Suva", identificationNumber = (string?)null },
            ("api/maintenance-jobs", "POST") => new { assetId = Guid.Empty, serviceType = "Preventive service", faultDescription = "Scheduled oil and filter change", assignedTo = "Demo technician", supplier = (string?)null, estimatedCost = 180, nextServiceDate = tomorrow.AddMonths(3).ToString("yyyy-MM-dd"), isPreventive = true, priority = "Normal" },
            ("api/bookings/{id}", "PUT") => new { branchId = Guid.Empty, customerId = Guid.Empty, assetId = Guid.Empty, startAt = tomorrow.ToString("yyyy-MM-dd") + "T08:00:00+12:00", endAt = tomorrow.AddDays(3).ToString("yyyy-MM-dd") + "T08:00:00+12:00", dailyRate = 120, notes = "Demo three-day hire", discountAmount = 0, taxRate = 12.5, depositRequired = 100, additionalCharges = 0, additionalChargesDescription = (string?)null },
            _ => null
        };
        if (example is null || operation.RequestBody?.Content is null) return;
        foreach (var media in operation.RequestBody.Content.Where(item => item.Key.Contains("json")))
            media.Value.Example = System.Text.Json.JsonSerializer.SerializeToNode(example);
        operation.Description += "\n\nExample uses fictional data. Replace every zero GUID with an existing record ID in your permitted branch/division, use a unique customer number, and review dates and rates before executing.";
    }
}
