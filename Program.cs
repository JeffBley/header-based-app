// Header Echo App
// Purpose: Validate that Entra App Proxy correctly injects HTTP headers from
//          Entra ID token claims (including CCP-injected custom claims).
//
// Security note: This app trusts all headers unconditionally. It must only be
// reachable from the App Proxy connector host. Use a Windows Firewall rule or
// IIS IP restriction to enforce this — never expose this app directly to users.

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// The headers we specifically want to call out in the UI.
// Names match the App Proxy SSO header mapping configuration.
var identityHeaders = new[]
{
    "X-MS-CLIENT-PRINCIPAL-NAME",   // UPN from user.userprincipalname
    "X-MS-CLIENT-PRINCIPAL-ID",     // Object ID from user.objectid
    "X-Favorite-Color",             // CCP-injected custom claim: favoriteColor
};

app.MapGet("/", (HttpRequest request) =>
{
    var html = BuildPage(request, identityHeaders);
    return Results.Content(html, "text/html");
});

app.Run();

static string BuildPage(HttpRequest request, string[] identityHeaders)
{
    // Collect all headers, HTML-encoding values to prevent XSS from
    // header content (even though this is an internal test app).
    var allHeaderRows = request.Headers
        .OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase)
        .Select(h =>
        {
            bool isIdentity = identityHeaders.Contains(h.Key, StringComparer.OrdinalIgnoreCase);
            string rowClass = isIdentity ? "identity-header" : "other-header";
            string name = HtmlEncode(h.Key);
            string value = HtmlEncode(h.Value.ToString());
            return $"""
                    <tr class="{rowClass}">
                        <td class="header-name">{name}</td>
                        <td class="header-value">{value}</td>
                    </tr>
                    """;
        });

    // Pull out the specific identity values for the summary card.
    string GetHeader(string name) =>
        request.Headers.TryGetValue(name, out var v) ? HtmlEncode(v.ToString()) : "<em>not present</em>";

    string upn           = GetHeader("X-MS-CLIENT-PRINCIPAL-NAME");
    string oid           = GetHeader("X-MS-CLIENT-PRINCIPAL-ID");
    string favoriteColor = GetHeader("X-Favorite-Color");

    // Generate a simple color swatch if the value looks like a CSS color name or hex.
    // We only use it as a background-color CSS value; XSS risk is low since it is
    // already HTML-encoded, but we additionally restrict to safe characters.
    string colorSwatch = "";
    if (request.Headers.TryGetValue("X-Favorite-Color", out var rawColor))
    {
        string safeColor = System.Text.RegularExpressions.Regex.Replace(
            rawColor.ToString(), @"[^a-zA-Z0-9#\-]", "");
        if (!string.IsNullOrWhiteSpace(safeColor))
        {
            colorSwatch = $"""<span class="color-swatch" style="background-color:{safeColor}" title="{HtmlEncode(safeColor)}"></span>""";
        }
    }

    return $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1" />
            <title>Header Echo — CCP Validation</title>
            <style>
                *, *::before, *::after { box-sizing: border-box; }
                body { font-family: 'Segoe UI', system-ui, sans-serif; margin: 0; background: #f3f4f6; color: #1f2937; }
                header { background: #0f6cbd; color: white; padding: 1.25rem 2rem; }
                header h1 { margin: 0; font-size: 1.5rem; font-weight: 600; }
                header p  { margin: 0.25rem 0 0; font-size: 0.875rem; opacity: 0.85; }
                main { padding: 2rem; max-width: 1100px; margin: 0 auto; }
                .card { background: white; border-radius: 8px; box-shadow: 0 1px 3px rgba(0,0,0,.1); padding: 1.5rem; margin-bottom: 1.5rem; }
                .card h2 { margin: 0 0 1rem; font-size: 1.1rem; border-bottom: 2px solid #e5e7eb; padding-bottom: .5rem; }
                .identity-grid { display: grid; grid-template-columns: max-content 1fr; gap: .5rem 1.5rem; align-items: center; }
                .identity-grid dt { font-weight: 600; color: #6b7280; font-size: .8rem; text-transform: uppercase; letter-spacing: .05em; }
                .identity-grid dd { margin: 0; font-size: .95rem; word-break: break-all; }
                .color-swatch { display: inline-block; width: 1.1rem; height: 1.1rem; border-radius: 3px; border: 1px solid #d1d5db; vertical-align: middle; margin-right: .4rem; }
                table { width: 100%; border-collapse: collapse; font-size: .875rem; }
                thead th { background: #f9fafb; text-align: left; padding: .6rem 1rem; font-size: .75rem; text-transform: uppercase; letter-spacing: .05em; color: #6b7280; border-bottom: 2px solid #e5e7eb; }
                tbody tr { border-bottom: 1px solid #f3f4f6; }
                tbody tr:last-child { border-bottom: none; }
                td { padding: .55rem 1rem; vertical-align: top; }
                .header-name { font-family: 'Cascadia Code', 'Consolas', monospace; white-space: nowrap; }
                .header-value { font-family: 'Cascadia Code', 'Consolas', monospace; word-break: break-all; color: #374151; }
                tr.identity-header { background: #eff6ff; }
                tr.identity-header .header-name { color: #1d4ed8; font-weight: 600; }
                .badge { display: inline-block; font-size: .7rem; font-weight: 600; padding: .15rem .45rem; border-radius: 999px; background: #dbeafe; color: #1d4ed8; margin-left: .5rem; vertical-align: middle; }
                .missing { color: #9ca3af; font-style: italic; }
            </style>
        </head>
        <body>
            <header>
                <h1>Header Echo App</h1>
                <p>CCP + App Proxy end-to-end validation tool &mdash; internal use only</p>
            </header>
            <main>
                <!-- Identity summary card -->
                <div class="card">
                    <h2>Identity Headers <span class="badge">App Proxy SSO</span></h2>
                    <dl class="identity-grid">
                        <dt>UPN</dt>
                        <dd>{{upn}}</dd>
                        <dt>Object ID</dt>
                        <dd>{{oid}}</dd>
                        <dt>Favorite Color</dt>
                        <dd>{{colorSwatch}}{{favoriteColor}}</dd>
                    </dl>
                </div>

                <!-- Full header dump -->
                <div class="card">
                    <h2>All Request Headers</h2>
                    <table>
                        <thead>
                            <tr>
                                <th>Header</th>
                                <th>Value</th>
                            </tr>
                        </thead>
                        <tbody>
                            {{string.Join(Environment.NewLine, allHeaderRows)}}
                        </tbody>
                    </table>
                </div>
            </main>
        </body>
        </html>
        """;
}

// Simple HTML encoder; avoids taking a dependency on HtmlEncoder in minimal context.
static string HtmlEncode(string input) =>
    input
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;")
        .Replace("'", "&#x27;");
