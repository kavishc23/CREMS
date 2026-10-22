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

                ### Management reporting
                **GET /api/reporting/workspace** returns the Reports & Performance workspace. Use inclusive Fiji dates
                (`from`, `to`) and optional branch, division, asset and record-status filters. The response includes
                KPI definitions, period asset profitability, historical utilization and downtime, and actionable
                reconciliation exceptions. Financial amounts are in FJD. See the endpoint description for period
                boundaries and the distinction between current snapshots and historical measures.

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
            DocumentReporting(operation, context.Description.RelativePath, context.Description.HttpMethod);
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

    private static void DocumentReporting(OpenApiOperation operation, string? path, string? method)
    {
        if (method != "GET") return;
        if (path == "api/dashboard/operations")
        {
            operation.Summary = "Current-month operations dashboard";
            operation.Description = "Fiji-calendar current and previous month net billed revenue (pre-tax, after applied credits), recorded maintenance cost, current fleet status, alerts, and recent activity. Fleet counts are current snapshots; use the reporting workspace for historical utilization and period asset profitability.";
            return;
        }
        if (path == "api/finance/workspace")
        {
            operation.Summary = "Finance invoice and payment workspace";
            operation.Description = "Returns the 200 most recent invoices and payments in the permitted scope, plus all-invoice revenue, gross invoiced, outstanding balance and unpaid-invoice totals. Revenue excludes tax, drafts and voids and subtracts applied credit-note subtotals. Outstanding is a current balance, not a period-end reconstruction. The displayed ledger rows are limited to 200; do not use them to reconstruct all-scope totals.";
            return;
        }
        if (path == "api/business-operations/asset-profitability")
        {
            operation.Summary = "Lifetime asset profitability (legacy)";
            operation.Description = "Returns lifetime operational asset contribution for the permitted scope. This endpoint is not date-filtered; use GET /api/reporting/workspace and its periodProfitability section for month-by-month asset, branch and division comparisons.";
            return;
        }
        if (path != "api/reporting/workspace") return;
        operation.Summary = "Management reporting workspace";
        operation.Description = """
            Returns the management reporting workspace for the caller's permitted assets and branches.

            **Dates:** `from` and `to` are inclusive calendar dates in `Pacific/Fiji` (`yyyy-MM-dd`).
            Defaults are the first day of the current Fiji month and today. Maximum range: 731 days.
            Bookings use scheduled pickup, invoices use issue date, payments use record date, and maintenance
            uses reported date. Asset status and the open exception worklists are current snapshots.

            **Scope:** `branchId`, `divisionId`, and `assetId` narrow the asset scope. Invoice and payment
            ledger rows include the whole booking if it contains a matching asset; `periodProfitability`
            allocates invoice revenue across booking assets by scheduled hire value. Status and payment-method
            filters apply to their respective ledger rows, not to the period P&L, utilization, or exceptions.

            **Response:** `summary` contains headline measures; `definitions` explains their formulas;
            `periodProfitability` contains asset rows and monthly, branch, and division trends;
            `historicalUtilization` contains capacity, hired, available, and maintenance-downtime days;
            `exceptions` contains source links for unpaid invoices, unallocated payments, missing invoices,
            unusual discounts, overdue returns, and maintenance overruns. `options` supplies filter choices.
            Monetary values are FJD. Profit is operational contribution, not audited net profit; outstanding
            is the current balance on invoices issued in the selected period, not a period-end reconstruction.
            """;
        var parameters = new Dictionary<string, string>
        {
            ["from"] = "Inclusive Fiji start date (yyyy-MM-dd). Defaults to the first day of the current month.",
            ["to"] = "Inclusive Fiji end date (yyyy-MM-dd). Defaults to today; maximum period is 731 days.",
            ["branchId"] = "Optional branch GUID within your permitted scope.",
            ["divisionId"] = "Optional division GUID within your permitted scope.",
            ["assetId"] = "Optional asset GUID within your permitted scope.",
            ["bookingStatus"] = "Filter booking ledger rows only; does not recalculate period P&L.",
            ["invoiceStatus"] = "Filter invoice ledger rows and its summary only; does not recalculate period P&L.",
            ["paymentMethod"] = "Filter payment ledger rows and its receipts summary only.",
            ["maintenanceStatus"] = "Filter maintenance ledger rows and its summary only; does not recalculate historical downtime."
        };
        if (operation.Parameters is not null)
            foreach (var parameter in operation.Parameters)
                if (parameter.Name is { } name && parameters.TryGetValue(name, out var description)) parameter.Description = description;
        operation.Responses ??= new OpenApiResponses();
        if (operation.Responses.TryGetValue("200", out var success))
            success.Description = "Reporting workspace with scoped ledgers, KPI definitions, period trends, utilization and exceptions.";
        else
            operation.Responses.Add("200", new OpenApiResponse { Description = "Reporting workspace with scoped ledgers, KPI definitions, period trends, utilization and exceptions." });
        operation.Responses.TryAdd("400", new OpenApiResponse { Description = "The reporting date range is reversed or exceeds 731 days." });
        operation.Responses.TryAdd("404", new OpenApiResponse { Description = "The requested asset is not in the caller's permitted asset scope." });
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
