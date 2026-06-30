using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Playwright;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables("RIS_");

builder.Services.Configure<RisApiOptions>(builder.Configuration.GetSection("RisApi"));
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .WithOrigins(
                "http://127.0.0.1:5173",
                "http://localhost:5173",
                "http://192.168.2.34:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
builder.Services.AddHttpClient("ris", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

var app = builder.Build();

app.UseCors();

app.MapGet("/", () => Results.Redirect("/health"));

app.MapGet("/health", (IConfiguration configuration) =>
{
    var options = configuration.GetSection("RisApi").Get<RisApiOptions>() ?? new RisApiOptions();
    return Results.Ok(new
    {
        ok = true,
        service = "RIS API Proxy",
        stationsConfigured = !string.IsNullOrWhiteSpace(options.StationsBaseUrl),
        journeysConfigured = !string.IsNullOrWhiteSpace(options.JourneysBaseUrl),
        authConfigured = options.HasAnyAuth()
    });
});

app.MapGet("/api/config/status", (IConfiguration configuration) =>
{
    var options = configuration.GetSection("RisApi").Get<RisApiOptions>() ?? new RisApiOptions();
    return Results.Ok(new
    {
        options.StationsBaseUrl,
        options.JourneysBaseUrl,
        authMode = options.AuthMode,
        apiKeyHeader = string.IsNullOrWhiteSpace(options.ApiKeyHeaderName) ? null : options.ApiKeyHeaderName,
        apiKeyConfigured = !string.IsNullOrWhiteSpace(options.ApiKey),
        bearerConfigured = !string.IsNullOrWhiteSpace(options.BearerToken),
        clientIdConfigured = !string.IsNullOrWhiteSpace(options.ClientId)
    });
});

app.MapGet("/api/ris/stations/{**path}", ProxyStations);
app.MapGet("/api/ris/journeys/{**path}", ProxyJourneys);
app.MapGet("/api/station-search/{query}", SearchStations);
app.MapGet("/api/search-train/{journeyNumber:int}", SearchTrain);
app.MapGet("/api/journey-by-number/{journeyNumber:int}", JourneyByNumber);
app.MapGet("/api/journey/{journeyId}", JourneyById);

app.MapPost("/api/ris/stations/{**path}", ProxyStations);
app.MapPost("/api/ris/journeys/{**path}", ProxyJourneys);
app.MapGet("/api/ris-infoplattform/import", () => Results.Json(new
{
    error = "Dieser Endpunkt erwartet POST mit JSON.",
    example = new
    {
        username = "RIS-Benutzername",
        password = "RIS-Passwort",
        trainNumbers = new[] { "4865" }
    }
}, statusCode: StatusCodes.Status405MethodNotAllowed));
app.MapPost("/api/ris-infoplattform/import", ImportRisInfoplattform);

app.Run();

static async Task<IResult> ImportRisInfoplattform(
    HttpContext context,
    IConfiguration configuration,
    CancellationToken cancellationToken)
{
    RisInfoplattformImportRequest? request;
    try
    {
        request = await JsonSerializer.DeserializeAsync<RisInfoplattformImportRequest>(
            context.Request.Body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            cancellationToken);
    }
    catch (JsonException ex)
    {
        return Results.BadRequest(new
        {
            error = "Ungueltige JSON-Anfrage.",
            detail = ex.Message,
            hint = "Bitte per POST einen JSON-Body mit username und password senden."
        });
    }

    if (string.IsNullOrWhiteSpace(request?.Username) || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest(new
        {
            error = "Benutzername und Passwort sind erforderlich.",
            requiredBody = new
            {
                username = "RIS-Benutzername",
                password = "RIS-Passwort",
                trainNumbers = new[] { "4865" }
            }
        });
    }

    var options = configuration.GetSection("RisInfoplattform").Get<RisInfoplattformOptions>() ?? new RisInfoplattformOptions();
    var timeout = Math.Max(15, options.TimeoutSeconds) * 1000;
    var capturedRequests = new ConcurrentQueue<string>();
    object? directLoginDiagnostics = null;

    var directLogin = await TryDirectRisInfoplattformLogin(request, options, cancellationToken);
    if (directLogin.IsSupported)
    {
        if (directLogin.TokenFound)
        {
            if (directLogin.Trains.Count > 0)
            {
                return Results.Json(new
                {
                    source = "RIS-Infoplattform",
                    tokenFound = true,
                    loginMode = "direct-api",
                    directLogin.Endpoints,
                    trains = directLogin.Trains
                });
            }

            directLoginDiagnostics = new
            {
                loginMode = "direct-api",
                directLogin.Endpoints,
                testedEndpoints = directLogin.TestedDataEndpoints
            };
        }

        if (directLogin.IsAuthoritativeFailure)
        {
            return Results.Json(new
            {
                error = "RIS-Infoplattform Login wurde vom Backend abgelehnt.",
                loginMode = "direct-api",
                directLogin.StatusCode,
                directLogin.ApiStatusCode,
                directLogin.Message,
                directLogin.ErrorDetails,
                hint = directLogin.ApiStatusCode == 16
                    ? "Dieser Account darf laut RIS-Infoplattform nur aus dem DB-Intranet genutzt werden. Bitte DB-VPN/Intranet pruefen."
                    : null,
                directLogin.Endpoints
            }, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    try
    {
        using var playwright = await Playwright.CreateAsync();
        var launchOptions = new BrowserTypeLaunchOptions
        {
            Headless = options.Headless,
            Timeout = timeout
        };

        if (!string.IsNullOrWhiteSpace(options.ChromePath) && File.Exists(options.ChromePath))
        {
            launchOptions.ExecutablePath = options.ChromePath;
        }

        await using var browser = await playwright.Chromium.LaunchAsync(launchOptions);
        var page = await browser.NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize { Width = 1365, Height = 900 }
        });

        page.Request += (_, browserRequest) =>
        {
            if (IsRisInfoplattformCandidate(browserRequest.Url))
            {
                capturedRequests.Enqueue(browserRequest.Url);
            }
        };

        await page.GotoAsync(options.LoginUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = timeout
        });

        var usernameFilled = await FillFirstAsync(page, request.Username, timeout / 3,
            "input[autocomplete='username']",
            "input[type='email']",
            "input[name*='user' i]",
            "input[name*='login' i]",
            "input[id*='user' i]",
            "input[id*='login' i]",
            "input[type='text']");

        var passwordFilled = await FillFirstAsync(page, request.Password, timeout / 3,
            "input[autocomplete='current-password']",
            "input[type='password']",
            "input[name*='pass' i]",
            "input[id*='pass' i]");

        if (!usernameFilled || !passwordFilled)
        {
            return Results.Json(new
            {
                error = "Loginformular wurde nicht erkannt.",
                loginUrl = page.Url,
                usernameFieldFound = usernameFilled,
                passwordFieldFound = passwordFilled,
                capturedEndpoints = UniqueCaptured(capturedRequests)
            }, statusCode: StatusCodes.Status502BadGateway);
        }

        var clicked = await ClickFirstAsync(page, timeout / 3,
            "button[type='submit']",
            "input[type='submit']",
            "button:has-text('Anmelden')",
            "button:has-text('Einloggen')",
            "button:has-text('Login')");

        if (!clicked)
        {
            await page.Keyboard.PressAsync("Enter");
        }

        await WaitQuietly(page, timeout);
        var storage = await ReadBrowserStorage(page);
        var tokenFound = HasLikelyAuthToken(storage);
        var stillLogin = page.Url.Contains("/login", StringComparison.OrdinalIgnoreCase)
            || page.Url.Contains("#/login", StringComparison.OrdinalIgnoreCase);

        if (stillLogin)
        {
            return Results.Json(new
            {
                error = "RIS-Infoplattform Login war nicht erfolgreich oder es wurde kein Token gefunden.",
                loginUrl = page.Url,
                tokenFound,
                directLogin = directLoginDiagnostics,
                capturedEndpoints = UniqueCaptured(capturedRequests),
                storageKeys = SafeStorageKeys(storage)
            }, statusCode: StatusCodes.Status502BadGateway);
        }

        var browserImported = await TryImportRisInfoplattformTrainsFromBrowser(
            page,
            request,
            storage,
            capturedRequests,
            cancellationToken);
        if (browserImported.Trains.Count > 0)
        {
            return Results.Json(new
            {
                source = "RIS-Infoplattform",
                tokenFound,
                loginMode = "browser-session",
                directLogin = directLoginDiagnostics,
                capturedEndpoints = UniqueCaptured(capturedRequests),
                testedEndpoints = browserImported.TestedEndpoints,
                trains = browserImported.Trains
            });
        }

        return Results.Json(new
        {
            error = "RIS-Infoplattform Login funktioniert. Die bekannten Fahrplan-Endpunkte akzeptieren aber auch die Browser-Sitzung noch nicht.",
            tokenFound,
            loginMode = "browser-session",
            directLogin = directLoginDiagnostics,
            capturedEndpoints = UniqueCaptured(capturedRequests),
            testedEndpoints = browserImported.TestedEndpoints,
            trains = Array.Empty<object>()
        }, statusCode: StatusCodes.Status501NotImplemented);
    }
    catch (PlaywrightException ex)
    {
        return Results.Json(new
        {
            error = "RIS-Infoplattform Browser-Automation fehlgeschlagen.",
            detail = ex.Message,
            capturedEndpoints = UniqueCaptured(capturedRequests)
        }, statusCode: StatusCodes.Status502BadGateway);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        return Results.Json(new
        {
            error = "RIS-Infoplattform Import ist unerwartet fehlgeschlagen.",
            detail = ex.Message,
            exceptionType = ex.GetType().Name,
            directLogin = directLoginDiagnostics,
            capturedEndpoints = UniqueCaptured(capturedRequests)
        }, statusCode: StatusCodes.Status502BadGateway);
    }
}

static async Task<RisInfoplattformLoginResult> TryDirectRisInfoplattformLogin(
    RisInfoplattformImportRequest request,
    RisInfoplattformOptions options,
    CancellationToken cancellationToken)
{
    var endpoints = new List<string>();
    var origin = GetOrigin(options.LoginUrl);
    if (origin is null)
    {
        return new RisInfoplattformLoginResult(false, false, false, null, null, null, null, endpoints.ToArray(), Array.Empty<string>(), new JsonArray());
    }

    using var handler = new HttpClientHandler
    {
        CookieContainer = new CookieContainer(),
        AutomaticDecompression = DecompressionMethods.All
    };
    using var client = new HttpClient(handler)
    {
        Timeout = TimeSpan.FromSeconds(Math.Max(15, options.TimeoutSeconds))
    };

    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126 Safari/537.36");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/plain, */*");
    client.DefaultRequestHeaders.Referrer = new Uri($"{origin}/#/login");

    try
    {
        endpoints.Add($"{origin}/");
        using (var warmup = await client.GetAsync($"{origin}/", cancellationToken))
        {
            _ = await warmup.Content.ReadAsStringAsync(cancellationToken);
        }

        endpoints.Add($"{origin}/api/v1/idm/login");
        using var loginResponse = await client.PostAsync(
            $"{origin}/api/v1/idm/login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["user"] = request.Username ?? "",
                ["pwd"] = request.Password ?? "",
                ["remember"] = "false"
            }),
            cancellationToken);

        var loginText = await loginResponse.Content.ReadAsStringAsync(cancellationToken);
        var loginJson = TryParseJsonNode(loginText);
        var tokenCandidates = FindLikelyTokens(loginJson);

        if (loginResponse.IsSuccessStatusCode)
        {
            endpoints.Add($"{origin}/api/v1/idm/refreshtoken");
            using (var refreshResponse = await client.GetAsync($"{origin}/api/v1/idm/refreshtoken", cancellationToken))
            {
                var refreshText = await refreshResponse.Content.ReadAsStringAsync(cancellationToken);
                var refreshJson = TryParseJsonNode(refreshText);
                tokenCandidates.AddRange(FindLikelyTokens(refreshJson));
            }

            endpoints.Add($"{origin}/api/v1/idm/configuration/token");
            using (var configurationTokenResponse = await client.GetAsync($"{origin}/api/v1/idm/configuration/token", cancellationToken))
            {
                var configurationTokenText = await configurationTokenResponse.Content.ReadAsStringAsync(cancellationToken);
                var configurationTokenJson = TryParseJsonNode(configurationTokenText);
                tokenCandidates.AddRange(FindLikelyTokens(configurationTokenJson));
            }

            if (tokenCandidates.Count > 0)
            {
                var authorizationCandidates = tokenCandidates.ToArray();
                foreach (var tokenCandidate in authorizationCandidates)
                {
                    foreach (var authMode in GetRisAuthModes())
                    {
                        var url = $"{origin}/api/v1/idm/configuration/token";
                        using var tokenRequest = new HttpRequestMessage(HttpMethod.Get, url);
                        ApplyRisDataAuth(tokenRequest, tokenCandidate.Value, authMode);
                        tokenRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                        tokenRequest.Headers.Referrer = new Uri($"{origin}/#/login");
                        tokenRequest.Headers.TryAddWithoutValidation("Origin", origin);

                        using var authorizedConfigurationTokenResponse = await client.SendAsync(tokenRequest, cancellationToken);
                        var authorizedConfigurationTokenText = await authorizedConfigurationTokenResponse.Content.ReadAsStringAsync(cancellationToken);
                        endpoints.Add($"{url} [{DescribeTokenCandidate(tokenCandidate)} via {authMode}] => {(int)authorizedConfigurationTokenResponse.StatusCode} {authorizedConfigurationTokenResponse.StatusCode}");
                        var authorizedConfigurationTokenJson = TryParseJsonNode(authorizedConfigurationTokenText);
                        tokenCandidates.AddRange(FindLikelyTokens(authorizedConfigurationTokenJson));
                    }
                }

                var backendConfigurationUrls = new[]
                {
                    "https://risinfoplattform-backend.pz.comp.db/ris-ipet-configuration/v1/configuration/token",
                    "https://risinfoplattform-backend.pz.comp.db/ris-ipet-configuration/v1/configuration"
                };
                foreach (var tokenCandidate in authorizationCandidates)
                {
                    foreach (var authMode in GetRisAuthModes())
                    {
                        foreach (var url in backendConfigurationUrls)
                        {
                            using var tokenRequest = new HttpRequestMessage(HttpMethod.Get, url);
                            ApplyRisDataAuth(tokenRequest, tokenCandidate.Value, authMode);
                            tokenRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                            tokenRequest.Headers.Referrer = new Uri($"{origin}/#/login");
                            tokenRequest.Headers.TryAddWithoutValidation("Origin", origin);

                            using var authorizedConfigurationTokenResponse = await client.SendAsync(tokenRequest, cancellationToken);
                            var authorizedConfigurationTokenText = await authorizedConfigurationTokenResponse.Content.ReadAsStringAsync(cancellationToken);
                            endpoints.Add($"{url} [{DescribeTokenCandidate(tokenCandidate)} via {authMode}] => {(int)authorizedConfigurationTokenResponse.StatusCode} {authorizedConfigurationTokenResponse.StatusCode}");
                            var authorizedConfigurationTokenJson = TryParseJsonNode(authorizedConfigurationTokenText);
                            tokenCandidates.AddRange(FindLikelyTokens(authorizedConfigurationTokenJson));
                        }
                    }
                }
            }

            endpoints.Add($"{origin}/api/v1/idm/configuration");
            using (var configurationResponse = await client.GetAsync($"{origin}/api/v1/idm/configuration", cancellationToken))
            {
                var configurationText = await configurationResponse.Content.ReadAsStringAsync(cancellationToken);
                tokenCandidates.AddRange(FindLikelyTokens(TryParseJsonNode(configurationText)));
            }
        }

        tokenCandidates = tokenCandidates
            .GroupBy(candidate => candidate.Value)
            .Select(group => group.OrderByDescending(candidate => ScoreToken(candidate.Source, candidate.Value)).First())
            .OrderByDescending(candidate => ScoreToken(candidate.Source, candidate.Value))
            .ToList();

        var apiStatusCode = GetInt(loginJson, "apiStatusCode");
        var message = GetString(loginJson, "message")
            ?? GetString(loginJson, "error")
            ?? GetString(loginJson, "detail")
            ?? GetString(loginJson, "statusText");

        var trains = new JsonArray();
        var testedDataEndpoints = new List<string>();
        if (tokenCandidates.Count > 0)
        {
            var imported = await TryImportRisInfoplattformTrains(
                client,
                origin,
                GetRisDataOrigins(origin),
                tokenCandidates,
                request,
                testedDataEndpoints,
                cancellationToken);
            foreach (var train in imported)
            {
                trains.Add(train?.DeepClone());
            }
        }

        return new RisInfoplattformLoginResult(
            true,
            tokenCandidates.Count > 0,
            !loginResponse.IsSuccessStatusCode,
            (int)loginResponse.StatusCode,
            apiStatusCode,
            message,
            SanitizeErrorDetails(loginJson),
            endpoints.ToArray(),
            testedDataEndpoints.ToArray(),
            trains);
    }
    catch (HttpRequestException ex)
    {
        return new RisInfoplattformLoginResult(false, false, false, null, null, ex.Message, null, endpoints.ToArray(), Array.Empty<string>(), new JsonArray());
    }
    catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
    {
        return new RisInfoplattformLoginResult(false, false, false, null, null, ex.Message, null, endpoints.ToArray(), Array.Empty<string>(), new JsonArray());
    }
}

static async Task<JsonArray> TryImportRisInfoplattformTrains(
    HttpClient client,
    string appOrigin,
    IReadOnlyList<string> dataOrigins,
    IReadOnlyList<TokenCandidate> tokenCandidates,
    RisInfoplattformImportRequest request,
    List<string> testedEndpoints,
    CancellationToken cancellationToken)
{
    var trains = new JsonArray();
    var numbers = request.TrainNumbers?.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct().ToArray();
    if (numbers is null || numbers.Length == 0)
    {
        return trains;
    }

    foreach (var number in numbers)
    {
        var autocomplete = await TryRisDataGet(
            client,
            appOrigin,
            dataOrigins,
            tokenCandidates,
            GetRisAutocompletePaths(),
            BuildAutocompleteQuery(request, number),
            testedEndpoints,
            cancellationToken);

        foreach (var autocompleteBody in BuildAutocompleteBodies(request, number))
        {
            if (autocomplete.Json is not null)
            {
                break;
            }

            autocomplete = await TryRisDataPost(
                client,
                appOrigin,
                dataOrigins,
                tokenCandidates,
                GetRisAutocompletePaths(),
                autocompleteBody,
                testedEndpoints,
                cancellationToken);
        }

        var journeyIds = ExtractJourneyIds(autocomplete.Json).ToArray();
        foreach (var journeyId in journeyIds)
        {
            var journey = await TryRisDataGet(
                client,
                appOrigin,
                dataOrigins,
                tokenCandidates,
                GetRisJourneyDetailPaths(),
                new[]
                {
                    new KeyValuePair<string, string?>("journeyID", journeyId)
                },
                testedEndpoints,
                cancellationToken);

            if (journey.Json is null)
            {
                journey = await TryRisDataPost(
                client,
                appOrigin,
                dataOrigins,
                tokenCandidates,
                GetRisJourneyDetailPaths(),
                new JsonObject
                {
                    ["journeyID"] = journeyId,
                    ["includeCanceled"] = true,
                    ["includeJourneyReferences"] = true,
                    ["includeExpiredDisruptions"] = true
                },
                testedEndpoints,
                cancellationToken);
            }

            var normalized = NormalizeInfoplattformJourney(journey.Json);
            if (normalized is not null)
            {
                trains.Add(normalized);
            }
        }
    }

    return trains;
}

static async Task<RisDataProbeResult> TryRisDataGet(
    HttpClient client,
    string appOrigin,
    IReadOnlyList<string> dataOrigins,
    IReadOnlyList<TokenCandidate> tokenCandidates,
    IEnumerable<string> paths,
    IEnumerable<KeyValuePair<string, string?>> query,
    List<string> testedEndpoints,
    CancellationToken cancellationToken)
{
    foreach (var baseUrl in BuildRisDataUrls(dataOrigins, paths))
    {
        var url = QueryHelpers.AddQueryString(baseUrl, query);
        foreach (var tokenCandidate in tokenCandidates)
        {
            foreach (var authMode in GetRisAuthModes())
            {
                using var message = new HttpRequestMessage(HttpMethod.Get, url);
                ApplyRisDataAuth(message, tokenCandidate.Value, authMode);
                message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                message.Headers.Referrer = new Uri($"{appOrigin}/#/train");
                message.Headers.TryAddWithoutValidation("Origin", appOrigin);

                using var response = await client.SendAsync(message, cancellationToken);
                var text = await response.Content.ReadAsStringAsync(cancellationToken);
                testedEndpoints.Add($"{url} [{DescribeTokenCandidate(tokenCandidate)} via {authMode}] => {(int)response.StatusCode} {response.StatusCode}: {TrimForProbe(text)}");
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                var json = TryParseJsonNode(text);
                if (json is not null)
                {
                    return new RisDataProbeResult(json, url);
                }
            }
        }
    }

    return new RisDataProbeResult(null, null);
}

static async Task<RisDataProbeResult> TryRisDataPost(
    HttpClient client,
    string appOrigin,
    IReadOnlyList<string> dataOrigins,
    IReadOnlyList<TokenCandidate> tokenCandidates,
    IEnumerable<string> paths,
    JsonObject body,
    List<string> testedEndpoints,
    CancellationToken cancellationToken)
{
    foreach (var url in BuildRisDataUrls(dataOrigins, paths))
    {
        foreach (var tokenCandidate in tokenCandidates)
        {
            foreach (var authMode in GetRisAuthModes())
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json")
                };
                ApplyRisDataAuth(message, tokenCandidate.Value, authMode);
                message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                message.Headers.Referrer = new Uri($"{appOrigin}/#/train");
                message.Headers.TryAddWithoutValidation("Origin", appOrigin);

                using var response = await client.SendAsync(message, cancellationToken);
                var text = await response.Content.ReadAsStringAsync(cancellationToken);
                testedEndpoints.Add($"{url} [{DescribeTokenCandidate(tokenCandidate)} via {authMode}] => {(int)response.StatusCode} {response.StatusCode}: {TrimForProbe(text)}");
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                var json = TryParseJsonNode(text);
                if (json is not null)
                {
                    return new RisDataProbeResult(json, url);
                }
            }
        }
    }

    return new RisDataProbeResult(null, null);
}

static string[] GetRisAutocompletePaths()
{
    return new[]
    {
        "/api/ris-ipet-data/v1/journey/number/autocomplete",
        "/api/ris-ipet-data/v1/journey"
    };
}

static string[] GetRisJourneyDetailPaths()
{
    return new[]
    {
        "/api/ris-ipet-data/v1/journey"
    };
}

static IEnumerable<KeyValuePair<string, string?>> BuildAutocompleteQuery(RisInfoplattformImportRequest request, string number)
{
    yield return new KeyValuePair<string, string?>("number", number);
    yield return new KeyValuePair<string, string?>("date", DateTime.Now.ToString("yyyy-MM-dd"));
    yield return new KeyValuePair<string, string?>("onlyDomesticJourneys", "false");

    var administrationId = (request.AdministrationIds ?? Array.Empty<string>())
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim())
        .FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(administrationId))
    {
        yield return new KeyValuePair<string, string?>("administrationID", administrationId);
    }
}

static IReadOnlyList<BrowserAuthAttempt> BuildBrowserAuthAttempts(IReadOnlyList<TokenCandidate> tokenCandidates)
{
    var attempts = new List<BrowserAuthAttempt>();
    foreach (var tokenCandidate in tokenCandidates)
    {
        foreach (var authMode in GetRisAuthModes())
        {
            attempts.Add(new BrowserAuthAttempt(DescribeTokenCandidate(tokenCandidate), authMode, tokenCandidate.Value));
        }
    }

    attempts.Add(new BrowserAuthAttempt("no-token", "None", ""));
    return attempts;
}

static string[] GetRisDataOrigins(string appOrigin)
{
    return new[]
    {
        appOrigin.TrimEnd('/'),
        "https://risinfoplattform-backend.pz.comp.db"
    }
    .Where(value => !string.IsNullOrWhiteSpace(value))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();
}

static IEnumerable<string> BuildRisDataUrls(IReadOnlyList<string> dataOrigins, IEnumerable<string> paths)
{
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var origin in dataOrigins)
    {
        foreach (var path in paths)
        {
            var candidatePath = IsRisBackendOrigin(origin) ? ToRisBackendPath(path) : path;
            var url = $"{origin.TrimEnd('/')}{candidatePath}";
            if (seen.Add(url))
            {
                yield return url;
            }
        }
    }
}

static bool IsRisBackendOrigin(string origin)
{
    return origin.Contains("risinfoplattform-backend", StringComparison.OrdinalIgnoreCase);
}

static string ToRisBackendPath(string path)
{
    return path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
        ? path[4..]
        : path;
}

static async Task<BrowserRisImportResult> TryImportRisInfoplattformTrainsFromBrowser(
    IPage page,
    RisInfoplattformImportRequest request,
    JsonNode? storage,
    ConcurrentQueue<string> capturedRequests,
    CancellationToken cancellationToken)
{
    var testedEndpoints = new List<string>();
    var trains = new JsonArray();
    var tokenCandidates = FindLikelyTokens(storage);
    var numbers = request.TrainNumbers?.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct().ToArray();
    if (numbers is null || numbers.Length == 0)
    {
        return new BrowserRisImportResult(trains, testedEndpoints.ToArray());
    }

    foreach (var number in numbers)
    {
        var autocomplete = await TryRisDataGetFromBrowser(
            page,
            tokenCandidates,
            GetRisAutocompletePaths(),
            BuildAutocompleteQuery(request, number),
            testedEndpoints,
            cancellationToken);

        foreach (var autocompleteBody in BuildAutocompleteBodies(request, number))
        {
            if (autocomplete.Json is not null)
            {
                break;
            }

            autocomplete = await TryRisDataPostFromBrowser(
                page,
                tokenCandidates,
                GetRisAutocompletePaths(),
                autocompleteBody,
                testedEndpoints,
                cancellationToken);
        }

        var directJourney = NormalizeInfoplattformJourney(autocomplete.Json);
        if (directJourney is not null && autocomplete.Json is JsonObject)
        {
            trains.Add(directJourney);
            continue;
        }

        var journeyIds = ExtractJourneyIds(autocomplete.Json).ToArray();
        foreach (var journeyId in journeyIds)
        {
            var journey = await TryRisDataGetFromBrowser(
                page,
                tokenCandidates,
                GetRisJourneyDetailPaths(),
                new[]
                {
                    new KeyValuePair<string, string?>("journeyID", journeyId)
                },
                testedEndpoints,
                cancellationToken);

            if (journey.Json is null)
            {
                journey = await TryRisDataPostFromBrowser(
                    page,
                    tokenCandidates,
                    GetRisJourneyDetailPaths(),
                    new JsonObject
                {
                    ["journeyID"] = journeyId,
                    ["includeCanceled"] = true,
                    ["includeJourneyReferences"] = true,
                    ["includeExpiredDisruptions"] = true
                },
                    testedEndpoints,
                    cancellationToken);
            }

            var normalized = NormalizeInfoplattformJourney(journey.Json);
            if (normalized is not null)
            {
                trains.Add(normalized);
            }
        }
    }

    foreach (var url in UniqueCaptured(capturedRequests))
    {
        if (url.Contains("ris-ipet-data", StringComparison.OrdinalIgnoreCase))
        {
            testedEndpoints.Add($"captured: {url}");
        }
    }

    return new BrowserRisImportResult(trains, testedEndpoints.ToArray());
}

static async Task<RisDataProbeResult> TryRisDataGetFromBrowser(
    IPage page,
    IReadOnlyList<TokenCandidate> tokenCandidates,
    IEnumerable<string> paths,
    IEnumerable<KeyValuePair<string, string?>> query,
    List<string> testedEndpoints,
    CancellationToken cancellationToken)
{
    var authAttempts = BuildBrowserAuthAttempts(tokenCandidates);
    var urlCandidates = new List<string>();
    foreach (var path in paths)
    {
        urlCandidates.Add(QueryHelpers.AddQueryString(path, query));
        urlCandidates.Add(QueryHelpers.AddQueryString($"https://risinfoplattform-backend.pz.comp.db{ToRisBackendPath(path)}", query));
    }

    foreach (var path in urlCandidates.Distinct(StringComparer.OrdinalIgnoreCase))
    {
        foreach (var authAttempt in authAttempts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resultText = await page.EvaluateAsync<string>(
                """
                async (args) => {
                  const headers = { "Accept": "application/json, text/plain, */*" };
                  const token = (args.token || "").trim();
                  const rawToken = token.toLowerCase().startsWith("bearer ") ? token.slice(7).trim() : token;
                  if (args.authMode === "Bearer" && rawToken) headers.Authorization = `Bearer ${rawToken}`;
                  if (args.authMode === "RawAuthorization" && token) headers.Authorization = token;
                  if (args.authMode === "AuthorizationJwt" && rawToken) headers.Authorization = `JWT ${rawToken}`;
                  if (args.authMode === "AuthorizationToken" && rawToken) headers.Authorization = `Token ${rawToken}`;
                  if (args.authMode === "JwtHeader" && rawToken) headers.jwt = rawToken;
                  if (args.authMode === "XJwtHeader" && rawToken) headers["X-JWT"] = rawToken;
                  if (args.authMode === "XAuthorizationBearer" && rawToken) headers["X-Authorization"] = `Bearer ${rawToken}`;
                  if (args.authMode === "XAuthorizationRaw" && token) headers["X-Authorization"] = token;
                  if (args.authMode === "XAuthToken" && rawToken) headers["X-Auth-Token"] = rawToken;
                  if (args.authMode === "AccessTokenHeader" && rawToken) headers["access-token"] = rawToken;
                  if (args.authMode === "AccessTokenCamelHeader" && rawToken) headers["AccessToken"] = rawToken;
                  try {
                    const response = await fetch(args.path, {
                      method: "GET",
                      headers,
                      credentials: "include"
                    });
                    const text = await response.text();
                    return JSON.stringify({ status: response.status, statusText: response.statusText, text });
                  } catch (error) {
                    return JSON.stringify({ status: 0, statusText: "FetchError", text: String(error && error.message ? error.message : error) });
                  }
                }
                """,
                new
                {
                    path,
                    authMode = authAttempt.AuthMode,
                    token = authAttempt.Token
                });

            var result = TryParseJsonNode(resultText);
            var status = GetInt(result, "status") ?? 0;
            var statusText = GetString(result, "statusText") ?? "";
            var text = GetString(result, "text") ?? "";
            testedEndpoints.Add($"{path} [{authAttempt.Label} via {authAttempt.AuthMode}] => {status} {statusText}: {TrimForProbe(text)}");
            if (status < 200 || status >= 300)
            {
                continue;
            }

            var json = TryParseJsonNode(text);
            if (json is not null)
            {
                return new RisDataProbeResult(json, path);
            }
        }
    }

    return new RisDataProbeResult(null, null);
}

static async Task<RisDataProbeResult> TryRisDataPostFromBrowser(
    IPage page,
    IReadOnlyList<TokenCandidate> tokenCandidates,
    IEnumerable<string> paths,
    JsonObject body,
    List<string> testedEndpoints,
    CancellationToken cancellationToken)
{
    var authAttempts = BuildBrowserAuthAttempts(tokenCandidates);

    var urlCandidates = new List<string>();
    foreach (var path in paths)
    {
        urlCandidates.Add(path);
        urlCandidates.Add($"https://risinfoplattform-backend.pz.comp.db{ToRisBackendPath(path)}");
    }

    foreach (var path in urlCandidates.Distinct(StringComparer.OrdinalIgnoreCase))
    {
        foreach (var authAttempt in authAttempts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resultText = await page.EvaluateAsync<string>(
                """
                async (args) => {
                  const headers = { "Content-Type": "application/json", "Accept": "application/json, text/plain, */*" };
                  const token = (args.token || "").trim();
                  const rawToken = token.toLowerCase().startsWith("bearer ") ? token.slice(7).trim() : token;
                  if (args.authMode === "Bearer" && rawToken) headers.Authorization = `Bearer ${rawToken}`;
                  if (args.authMode === "RawAuthorization" && token) headers.Authorization = token;
                  if (args.authMode === "AuthorizationJwt" && rawToken) headers.Authorization = `JWT ${rawToken}`;
                  if (args.authMode === "AuthorizationToken" && rawToken) headers.Authorization = `Token ${rawToken}`;
                  if (args.authMode === "JwtHeader" && rawToken) headers.jwt = rawToken;
                  if (args.authMode === "XJwtHeader" && rawToken) headers["X-JWT"] = rawToken;
                  if (args.authMode === "XAuthorizationBearer" && rawToken) headers["X-Authorization"] = `Bearer ${rawToken}`;
                  if (args.authMode === "XAuthorizationRaw" && token) headers["X-Authorization"] = token;
                  if (args.authMode === "XAuthToken" && rawToken) headers["X-Auth-Token"] = rawToken;
                  if (args.authMode === "AccessTokenHeader" && rawToken) headers["access-token"] = rawToken;
                  if (args.authMode === "AccessTokenCamelHeader" && rawToken) headers["AccessToken"] = rawToken;
                  try {
                    const response = await fetch(args.path, {
                      method: "POST",
                      headers,
                      body: args.bodyJson,
                      credentials: "include"
                    });
                    const text = await response.text();
                    return JSON.stringify({ status: response.status, statusText: response.statusText, text });
                  } catch (error) {
                    return JSON.stringify({ status: 0, statusText: "FetchError", text: String(error && error.message ? error.message : error) });
                  }
                }
                """,
                new
                {
                    path,
                    bodyJson = body.ToJsonString(),
                    authMode = authAttempt.AuthMode,
                    token = authAttempt.Token
                });

            var result = TryParseJsonNode(resultText);
            var status = GetInt(result, "status") ?? 0;
            var statusText = GetString(result, "statusText") ?? "";
            var text = GetString(result, "text") ?? "";
            testedEndpoints.Add($"{path} [{authAttempt.Label} via {authAttempt.AuthMode}] => {status} {statusText}: {TrimForProbe(text)}");
            if (status < 200 || status >= 300)
            {
                continue;
            }

            var json = TryParseJsonNode(text);
            if (json is not null)
            {
                return new RisDataProbeResult(json, path);
            }
        }
    }

    return new RisDataProbeResult(null, null);
}

static void ApplyRisDataAuth(HttpRequestMessage message, string token, string authMode)
{
    switch (authMode)
    {
        case "Bearer":
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", StripBearerPrefix(token));
            break;
        case "RawAuthorization":
            message.Headers.TryAddWithoutValidation("Authorization", token);
            break;
        case "AuthorizationJwt":
            message.Headers.TryAddWithoutValidation("Authorization", $"JWT {StripBearerPrefix(token)}");
            break;
        case "AuthorizationToken":
            message.Headers.TryAddWithoutValidation("Authorization", $"Token {StripBearerPrefix(token)}");
            break;
        case "JwtHeader":
            message.Headers.TryAddWithoutValidation("jwt", StripBearerPrefix(token));
            break;
        case "XJwtHeader":
            message.Headers.TryAddWithoutValidation("X-JWT", StripBearerPrefix(token));
            break;
        case "XAuthorizationBearer":
            message.Headers.TryAddWithoutValidation("X-Authorization", $"Bearer {StripBearerPrefix(token)}");
            break;
        case "XAuthorizationRaw":
            message.Headers.TryAddWithoutValidation("X-Authorization", token);
            break;
        case "XAuthToken":
            message.Headers.TryAddWithoutValidation("X-Auth-Token", StripBearerPrefix(token));
            break;
        case "AccessTokenHeader":
            message.Headers.TryAddWithoutValidation("access-token", StripBearerPrefix(token));
            break;
        case "AccessTokenCamelHeader":
            message.Headers.TryAddWithoutValidation("AccessToken", StripBearerPrefix(token));
            break;
    }
}

static string[] GetRisAuthModes()
{
    return new[]
    {
        "Bearer",
        "RawAuthorization",
        "AuthorizationJwt",
        "AuthorizationToken",
        "JwtHeader",
        "XJwtHeader",
        "XAuthorizationBearer",
        "XAuthorizationRaw",
        "XAuthToken",
        "AccessTokenHeader",
        "AccessTokenCamelHeader"
    };
}

static IEnumerable<JsonObject> BuildAutocompleteBodies(RisInfoplattformImportRequest request, string number)
{
    var date = DateTime.Now.ToString("yyyy-MM-dd");
    var dateTime = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
    var administrationIds = (request.AdministrationIds ?? Array.Empty<string>())
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim())
        .Concat(new[] { "80", "800", "81", "85", "00" })
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    yield return new JsonObject
    {
        ["number"] = number,
        ["date"] = date,
        ["onlyDomesticJourneys"] = false
    };

    yield return new JsonObject
    {
        ["number"] = number,
        ["date"] = dateTime,
        ["onlyDomesticJourneys"] = false
    };

    yield return new JsonObject
    {
        ["zugnummer"] = number,
        ["date"] = date,
        ["onlyDomesticJourneys"] = false
    };

    foreach (var administrationId in administrationIds)
    {
        yield return new JsonObject
        {
            ["number"] = number,
            ["date"] = date,
            ["onlyDomesticJourneys"] = false,
            ["administrationID"] = administrationId
        };
    }
}

static string StripBearerPrefix(string token)
{
    const string bearerPrefix = "Bearer ";
    return token.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
        ? token[bearerPrefix.Length..].Trim()
        : token.Trim();
}

static string DescribeTokenCandidate(TokenCandidate candidate)
{
    var kind = LooksLikeJwt(StripBearerPrefix(candidate.Value)) ? "jwt" : "text";
    return $"{candidate.Source}:{kind}:{StripBearerPrefix(candidate.Value).Length}";
}

static List<TokenCandidate> FindLikelyTokens(JsonNode? node)
{
    var candidates = new List<TokenCandidate>();
    CollectLikelyTokens(node, "$", candidates);
    return candidates
        .GroupBy(candidate => candidate.Value)
        .Select(group => group.OrderByDescending(candidate => ScoreToken(candidate.Source, candidate.Value)).First())
        .OrderByDescending(candidate => ScoreToken(candidate.Source, candidate.Value))
        .ToList();
}

static void CollectLikelyTokens(JsonNode? node, string path, List<TokenCandidate> candidates)
{
    if (node is JsonObject obj)
    {
        foreach (var property in obj)
        {
            var propertyPath = $"{path}.{property.Key}";
            if (property.Value is JsonValue value
                && value.TryGetValue<string>(out var stringValue)
                && IsLikelyToken(propertyPath, stringValue))
            {
                candidates.Add(new TokenCandidate(propertyPath, stringValue.Trim()));
            }

            CollectLikelyTokens(property.Value, propertyPath, candidates);
        }
    }
    else if (node is JsonArray array)
    {
        for (var index = 0; index < array.Count; index++)
        {
            CollectLikelyTokens(array[index], $"{path}[{index}]", candidates);
        }
    }
    else if (node is JsonValue value
        && value.TryGetValue<string>(out var stringValue)
        && IsLikelyToken(path, stringValue))
    {
        candidates.Add(new TokenCandidate(path, stringValue.Trim()));
    }
}

static bool IsLikelyToken(string path, string value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return false;
    }

    var normalized = StripBearerPrefix(value);
    if (normalized.Length < 20)
    {
        return false;
    }

    return LooksLikeJwt(normalized)
        || path.Contains("token", StringComparison.OrdinalIgnoreCase)
        || path.Contains("jwt", StringComparison.OrdinalIgnoreCase)
        || path.Contains("access", StringComparison.OrdinalIgnoreCase)
        || path.Contains("auth", StringComparison.OrdinalIgnoreCase);
}

static int ScoreToken(string path, string value)
{
    var normalized = StripBearerPrefix(value);
    var score = 0;
    if (LooksLikeJwt(normalized))
    {
        score += 100;
    }

    if (path.Contains("accessToken", StringComparison.OrdinalIgnoreCase) || path.Contains("access_token", StringComparison.OrdinalIgnoreCase))
    {
        score += 50;
    }

    if (path.Contains("idToken", StringComparison.OrdinalIgnoreCase) || path.Contains("id_token", StringComparison.OrdinalIgnoreCase))
    {
        score += 40;
    }

    if (path.Contains("jwt", StringComparison.OrdinalIgnoreCase))
    {
        score += 30;
    }

    if (path.Contains("refresh", StringComparison.OrdinalIgnoreCase))
    {
        score -= 20;
    }

    return score + Math.Min(normalized.Length / 20, 10);
}

static string TrimForProbe(string value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return "";
    }

    var normalized = value.Replace("\r", " ").Replace("\n", " ").Trim();
    return normalized.Length <= 220 ? normalized : normalized[..220];
}

static IEnumerable<string> ExtractJourneyIds(JsonNode? node)
{
    if (node is null)
    {
        yield break;
    }

    var journeys = node["journeys"]?.AsArray()
        ?? node["items"]?.AsArray()
        ?? node["results"]?.AsArray()
        ?? (node is JsonArray array ? array : null);

    if (journeys is null)
    {
        var direct = GetString(node, "journeyID") ?? GetString(node, "journeyId");
        if (!string.IsNullOrWhiteSpace(direct))
        {
            yield return direct;
        }

        yield break;
    }

    foreach (var item in journeys)
    {
        var id = GetString(item, "journeyID") ?? GetString(item, "journeyId") ?? GetString(item, "id");
        if (!string.IsNullOrWhiteSpace(id))
        {
            yield return id;
        }
    }
}

static JsonObject? NormalizeInfoplattformJourney(JsonNode? json)
{
    var journey = json?["Journey"] ?? json?["journey"] ?? json;
    if (journey is null)
    {
        return null;
    }

    if (journey["events"] is not null || journey["info"] is not null)
    {
        return NormalizeJourney(journey);
    }

    var zuglauf = json?["Service"]?["Zuglauf"] ?? json?["Zuglauf"];
    if (zuglauf is null)
    {
        return new JsonObject
        {
            ["source"] = "RIS-Infoplattform",
            ["raw"] = json?.DeepClone()
        };
    }

    var events = zuglauf["ZE"]?.AsArray() ?? new JsonArray();
    var stops = new JsonArray();
    foreach (var item in events)
    {
        var name = GetString(item?["Bf"], "Langname") ?? GetString(item?["Bf"], "Name") ?? GetString(item, "BfName");
        if (string.IsNullOrWhiteSpace(name))
        {
            continue;
        }

        stops.Add(new JsonArray(
            name,
            FormatTime(GetString(item?["ZeitAn"], "Soll") ?? GetString(item?["Zeit"], "Soll")),
            FormatTime(GetString(item?["ZeitAb"], "Soll") ?? GetString(item?["Zeit"], "Soll")),
            GetString(item, "Gleis") ?? GetString(item, "Gl")));
    }

    var first = stops.FirstOrDefault()?.AsArray();
    var last = stops.LastOrDefault()?.AsArray();
    var line = GetString(zuglauf, "Gattung") ?? GetString(zuglauf?["Zug"], "Gattung") ?? "Zug";
    var number = GetString(zuglauf, "Nr") ?? GetString(zuglauf?["Zug"], "Nr") ?? GetString(json?["Service"], "IdZNr") ?? "";

    return new JsonObject
    {
        ["source"] = "RIS-Infoplattform",
        ["journeyID"] = GetString(json?["Service"], "FahrtId") ?? GetString(journey, "journeyID"),
        ["number"] = number,
        ["line"] = string.IsNullOrWhiteSpace(number) ? line : $"{line} {number}",
        ["from"] = first?[0]?.GetValue<string>(),
        ["to"] = last?[0]?.GetValue<string>(),
        ["duration"] = "",
        ["stops"] = stops,
        ["raw"] = json?.DeepClone()
    };
}

static string? GetOrigin(string url)
{
    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
    {
        return null;
    }

    return $"{uri.Scheme}://{uri.Host}";
}

static JsonNode? TryParseJsonNode(string value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    try
    {
        return JsonNode.Parse(value);
    }
    catch (JsonException)
    {
        return null;
    }
}

static string? FindLikelyToken(JsonNode? node)
{
    if (node is JsonObject obj)
    {
        foreach (var property in obj)
        {
            var key = property.Key;
            if (property.Value is JsonValue value
                && value.TryGetValue<string>(out var stringValue)
                && !string.IsNullOrWhiteSpace(stringValue)
                && (key.Contains("token", StringComparison.OrdinalIgnoreCase)
                    || key.Contains("jwt", StringComparison.OrdinalIgnoreCase)
                    || LooksLikeJwt(stringValue)))
            {
                return stringValue;
            }

            var nested = FindLikelyToken(property.Value);
            if (!string.IsNullOrWhiteSpace(nested))
            {
                return nested;
            }
        }
    }
    else if (node is JsonArray array)
    {
        foreach (var item in array)
        {
            var nested = FindLikelyToken(item);
            if (!string.IsNullOrWhiteSpace(nested))
            {
                return nested;
            }
        }
    }
    else if (node is JsonValue value
        && value.TryGetValue<string>(out var stringValue)
        && LooksLikeJwt(stringValue))
    {
        return stringValue;
    }

    return null;
}

static bool LooksLikeJwt(string value)
{
    var parts = value.Split('.');
    return parts.Length == 3 && parts.All(part => part.Length > 10 && part.All(IsBase64UrlChar));
}

static bool IsBase64UrlChar(char value)
{
    return value is >= 'A' and <= 'Z'
        || value is >= 'a' and <= 'z'
        || value is >= '0' and <= '9'
        || value == '-'
        || value == '_';
}

static object? SanitizeErrorDetails(JsonNode? node)
{
    var details = node?["errorDetails"] ?? node?["errors"] ?? node?["details"];
    if (details is null)
    {
        return null;
    }

    return JsonSerializer.Deserialize<object>(details.ToJsonString());
}

static async Task<bool> FillFirstAsync(IPage page, string value, float timeout, params string[] selectors)
{
    foreach (var selector in selectors)
    {
        var locator = page.Locator(selector);
        if (await locator.CountAsync() == 0)
        {
            continue;
        }

        try
        {
            await locator.Nth(0).FillAsync(value, new LocatorFillOptions { Timeout = timeout });
            return true;
        }
        catch (PlaywrightException)
        {
            // Try the next likely selector.
        }
    }

    return false;
}

static async Task<bool> ClickFirstAsync(IPage page, float timeout, params string[] selectors)
{
    foreach (var selector in selectors)
    {
        var locator = page.Locator(selector);
        if (await locator.CountAsync() == 0)
        {
            continue;
        }

        try
        {
            await locator.Nth(0).ClickAsync(new LocatorClickOptions { Timeout = timeout });
            return true;
        }
        catch (PlaywrightException)
        {
            // Try the next likely selector.
        }
    }

    return false;
}

static async Task WaitQuietly(IPage page, float timeout)
{
    try
    {
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = timeout });
    }
    catch (PlaywrightException)
    {
        // Some SPAs keep sockets open. Give the app a short grace period instead.
    }

    await page.WaitForTimeoutAsync(2500);
}

static async Task<JsonNode?> ReadBrowserStorage(IPage page)
{
    var json = await page.EvaluateAsync<string>(
        """
        () => {
          const read = (storage) => {
            const result = {};
            for (let i = 0; i < storage.length; i += 1) {
              const key = storage.key(i);
              result[key] = storage.getItem(key);
            }
            return result;
          };
          return JSON.stringify({
            href: location.href,
            title: document.title,
            local: read(localStorage),
            session: read(sessionStorage)
          });
        }
        """);

    return JsonNode.Parse(json);
}

static bool HasLikelyAuthToken(JsonNode? storage)
{
    foreach (var value in FlattenStringValues(storage))
    {
        if (value.Contains("access_token", StringComparison.OrdinalIgnoreCase)
            || value.Contains("id_token", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Bearer ", StringComparison.OrdinalIgnoreCase)
            || value.Contains("eyJ", StringComparison.Ordinal))
        {
            return true;
        }
    }

    return false;
}

static IEnumerable<string> FlattenStringValues(JsonNode? node)
{
    if (node is JsonObject obj)
    {
        foreach (var property in obj)
        {
            foreach (var value in FlattenStringValues(property.Value))
            {
                yield return value;
            }
        }
    }
    else if (node is JsonArray array)
    {
        foreach (var item in array)
        {
            foreach (var value in FlattenStringValues(item))
            {
                yield return value;
            }
        }
    }
    else if (node is JsonValue valueNode && valueNode.TryGetValue<string>(out var value))
    {
        yield return value;
    }
}

static object SafeStorageKeys(JsonNode? storage)
{
    static string[] Keys(JsonNode? node)
    {
        return node is JsonObject obj ? obj.Select(item => item.Key).ToArray() : Array.Empty<string>();
    }

    return new
    {
        local = Keys(storage?["local"]),
        session = Keys(storage?["session"])
    };
}

static string[] UniqueCaptured(ConcurrentQueue<string> capturedRequests)
{
    return capturedRequests
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(50)
        .ToArray();
}

static bool IsRisInfoplattformCandidate(string url)
{
    return url.Contains("ris-info", StringComparison.OrdinalIgnoreCase)
        || url.Contains("operational", StringComparison.OrdinalIgnoreCase)
        || url.Contains("journey", StringComparison.OrdinalIgnoreCase)
        || url.Contains("train", StringComparison.OrdinalIgnoreCase)
        || url.Contains("station", StringComparison.OrdinalIgnoreCase);
}

static Task<IResult> ProxyStations(
    string? path,
    HttpContext context,
    IHttpClientFactory clientFactory,
    IConfiguration configuration,
    CancellationToken cancellationToken)
{
    var options = configuration.GetSection("RisApi").Get<RisApiOptions>() ?? new RisApiOptions();
    return ProxyToRis(options.StationsBaseUrl, path, context, clientFactory, options, cancellationToken);
}

static Task<IResult> ProxyJourneys(
    string? path,
    HttpContext context,
    IHttpClientFactory clientFactory,
    IConfiguration configuration,
    CancellationToken cancellationToken)
{
    var options = configuration.GetSection("RisApi").Get<RisApiOptions>() ?? new RisApiOptions();
    return ProxyToRis(options.JourneysBaseUrl, path, context, clientFactory, options, cancellationToken);
}

static async Task<IResult> SearchStations(
    string query,
    IHttpClientFactory clientFactory,
    IConfiguration configuration,
    CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
    {
        return Results.Json(Array.Empty<object>());
    }

    var options = configuration.GetSection("RisApi").Get<RisApiOptions>() ?? new RisApiOptions();
    if (string.IsNullOrWhiteSpace(options.StationsBaseUrl))
    {
        return Results.Problem("RIS StationsBaseUrl ist nicht konfiguriert.", statusCode: StatusCodes.Status500InternalServerError);
    }

    var response = await GetRisJson(
        options.StationsBaseUrl,
        $"stop-places/by-name/{Uri.EscapeDataString(query.Trim())}",
        new Dictionary<string, string?>
        {
            ["limit"] = "25",
            ["groupByStation"] = "true"
        },
        clientFactory,
        options,
        cancellationToken);

    if (response.Error is not null)
    {
        return response.Error;
    }

    return Results.Json(NormalizeStations(response.Json, LoadEvaToRl100()));
}

static async Task<IResult> SearchTrain(
    int journeyNumber,
    string? date,
    IHttpClientFactory clientFactory,
    IConfiguration configuration,
    CancellationToken cancellationToken)
{
    var options = configuration.GetSection("RisApi").Get<RisApiOptions>() ?? new RisApiOptions();
    var findResult = await FindJourneys(journeyNumber, date, clientFactory, options, cancellationToken);
    if (findResult.Error is not null)
    {
        return findResult.Error;
    }

    var journeys = findResult.Json?["journeys"]?.AsArray();
    var normalized = new JsonArray();
    if (journeys is not null)
    {
        foreach (var journey in journeys)
        {
            if (journey is null)
            {
                continue;
            }

            var info = journey["info"];
            var transport = info?["transportAtStart"];
            var relation = journey["journeyRelation"];
            normalized.Add(new JsonObject
            {
                ["journeyID"] = GetString(journey, "journeyID"),
                ["number"] = GetString(info, "headerJourneyNumber") ?? GetString(transport, "journeyNumber") ?? journeyNumber.ToString(),
                ["line"] = BuildLine(transport, info, journeyNumber),
                ["from"] = StopName(info?["origin"]),
                ["to"] = StopName(info?["destination"]),
                ["startTime"] = FormatTime(GetString(relation, "startTime") ?? GetString(info, "scheduledStartTime")),
                ["endTime"] = FormatTime(GetString(relation, "endTime")),
                ["date"] = date ?? DateTime.Now.ToString("yyyy-MM-dd")
            });
        }
    }

    return Results.Json(new JsonObject
    {
        ["total"] = GetInt(findResult.Json, "total") ?? normalized.Count,
        ["journeys"] = normalized,
        ["raw"] = findResult.Json?.DeepClone()
    });
}

static async Task<IResult> JourneyByNumber(
    int journeyNumber,
    string? date,
    IHttpClientFactory clientFactory,
    IConfiguration configuration,
    CancellationToken cancellationToken)
{
    var options = configuration.GetSection("RisApi").Get<RisApiOptions>() ?? new RisApiOptions();
    var findResult = await FindJourneys(journeyNumber, date, clientFactory, options, cancellationToken);
    if (findResult.Error is not null)
    {
        return findResult.Error;
    }

    var journeyId = findResult.Json?["journeys"]?.AsArray().FirstOrDefault()?["journeyID"]?.GetValue<string>();
    if (string.IsNullOrWhiteSpace(journeyId))
    {
        return Results.NotFound(new { message = $"Für die Zugnummer {journeyNumber} wurde kein RIS-Zug gefunden." });
    }

    return await JourneyById(journeyId, clientFactory, configuration, cancellationToken);
}

static async Task<IResult> JourneyById(
    string journeyId,
    IHttpClientFactory clientFactory,
    IConfiguration configuration,
    CancellationToken cancellationToken)
{
    var options = configuration.GetSection("RisApi").Get<RisApiOptions>() ?? new RisApiOptions();
    if (string.IsNullOrWhiteSpace(options.JourneysBaseUrl))
    {
        return Results.Problem("RIS JourneysBaseUrl ist nicht konfiguriert.", statusCode: StatusCodes.Status500InternalServerError);
    }

    var query = new Dictionary<string, string?>
    {
        ["includeReferences"] = "false",
        ["separateCancelled"] = "true",
        ["languages"] = "DE"
    };

    var response = await GetRisJson(options.JourneysBaseUrl, Uri.EscapeDataString(journeyId), query, clientFactory, options, cancellationToken);
    if (response.Error is not null)
    {
        return response.Error;
    }

    return Results.Json(NormalizeJourney(response.Json));
}

static async Task<IResult> ProxyToRis(
    string? baseUrl,
    string? path,
    HttpContext context,
    IHttpClientFactory clientFactory,
    RisApiOptions options,
    CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(baseUrl))
    {
        return Results.Problem("RIS base URL ist nicht konfiguriert.", statusCode: StatusCodes.Status500InternalServerError);
    }

    if (!Uri.TryCreate(baseUrl.TrimEnd('/') + "/" + (path ?? "").TrimStart('/'), UriKind.Absolute, out var uri))
    {
        return Results.Problem("RIS Ziel-URL ist ungültig.", statusCode: StatusCodes.Status500InternalServerError);
    }

    var targetUri = QueryHelpers.AddQueryString(
        uri.ToString(),
        context.Request.Query
            .Where(item => !item.Key.StartsWith("_", StringComparison.Ordinal))
            .ToDictionary(item => item.Key, item => (string?)item.Value.ToString()));

    using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUri);
    ApplyAuth(request, options);
    ApplyForwardHeaders(request, context);

    if (HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPut(context.Request.Method) || HttpMethods.IsPatch(context.Request.Method))
    {
        request.Content = new StreamContent(context.Request.Body);
        if (!string.IsNullOrWhiteSpace(context.Request.ContentType))
        {
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(context.Request.ContentType);
        }
    }

    var client = clientFactory.CreateClient("ris");
    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
    var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
    return new ProxyResponseResult(bytes, contentType, (int)response.StatusCode);
}

static Task<RisJsonResponse> FindJourneys(
    int journeyNumber,
    string? date,
    IHttpClientFactory clientFactory,
    RisApiOptions options,
    CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(options.JourneysBaseUrl))
    {
        return Task.FromResult(new RisJsonResponse(null, Results.Problem("RIS JourneysBaseUrl ist nicht konfiguriert.", statusCode: StatusCodes.Status500InternalServerError)));
    }

    var query = new Dictionary<string, string?>
    {
        ["journeyNumber"] = journeyNumber.ToString(),
        ["date"] = string.IsNullOrWhiteSpace(date) ? DateTime.Now.ToString("yyyy-MM-dd") : date,
        ["limit"] = "20"
    };

    return GetRisJson(options.JourneysBaseUrl, "find", query, clientFactory, options, cancellationToken);
}

static async Task<RisJsonResponse> GetRisJson(
    string baseUrl,
    string path,
    Dictionary<string, string?> query,
    IHttpClientFactory clientFactory,
    RisApiOptions options,
    CancellationToken cancellationToken)
{
    if (!Uri.TryCreate(baseUrl.TrimEnd('/') + "/" + path.TrimStart('/'), UriKind.Absolute, out var uri))
    {
        return new RisJsonResponse(null, Results.Problem("RIS Ziel-URL ist ungültig.", statusCode: StatusCodes.Status500InternalServerError));
    }

    var targetUri = QueryHelpers.AddQueryString(uri.ToString(), query);
    using var request = new HttpRequestMessage(HttpMethod.Get, targetUri);
    ApplyAuth(request, options);
    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.de.db.ris+json"));
    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    request.Headers.UserAgent.ParseAdd("RIS-Communicator-LocalProxy/1.0");

    var client = clientFactory.CreateClient("ris");
    using var response = await client.SendAsync(request, cancellationToken);
    var content = await response.Content.ReadAsStringAsync(cancellationToken);
    if (!response.IsSuccessStatusCode)
    {
        return new RisJsonResponse(null, Results.Json(new
        {
            message = "RIS Anfrage wurde abgelehnt.",
            status = (int)response.StatusCode,
            body = content
        }, statusCode: (int)response.StatusCode));
    }

    var json = string.IsNullOrWhiteSpace(content) ? null : JsonNode.Parse(content);
    return new RisJsonResponse(json, null);
}

static JsonObject NormalizeJourney(JsonNode? journey)
{
    var info = journey?["info"];
    var events = journey?["events"]?.AsArray();
    var transport = info?["transportAtStart"] ?? events?.FirstOrDefault()?["transport"];
    var stops = BuildStops(events);
    var number = GetString(info, "headerJourneyNumber") ?? GetString(transport, "journeyNumber") ?? "";
    var line = BuildLine(transport, info, int.TryParse(number, out var parsedNumber) ? parsedNumber : 0);

    return new JsonObject
    {
        ["source"] = "RIS",
        ["journeyID"] = GetString(journey, "journeyID"),
        ["number"] = number,
        ["line"] = line,
        ["from"] = StopName(info?["origin"]) ?? stops.FirstOrDefault()?.Station,
        ["to"] = StopName(info?["destination"]) ?? stops.LastOrDefault()?.Station,
        ["duration"] = BuildDuration(stops),
        ["stops"] = new JsonArray(stops.Select(stop => new JsonArray(stop.Station, stop.Arrival, stop.Departure, stop.Platform)).ToArray<JsonNode?>()),
        ["raw"] = journey?.DeepClone()
    };
}

static JsonArray NormalizeStations(JsonNode? json, IReadOnlyDictionary<string, string> evaToRl100)
{
    var source = ExtractStationCandidates(json);
    var stations = new JsonArray();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var item in source)
    {
        var name = StopName(item)
            ?? GetString(item, "stationName")
            ?? GetString(item, "nameLong")
            ?? GetString(item, "nameShort");

        if (string.IsNullOrWhiteSpace(name))
        {
            continue;
        }

        var evaNumber = GetString(item, "evaNumber")
            ?? GetString(item, "eva")
            ?? GetString(item, "stopPlaceID")
            ?? GetString(item, "stopPlaceId")
            ?? GetString(item, "id");
        var ril100 = LookupRl100(evaNumber, evaToRl100) ?? ExtractRil100(item);
        var key = $"{name}|{evaNumber}|{ril100}";
        if (!seen.Add(key))
        {
            continue;
        }

        stations.Add(new JsonObject
        {
            ["name"] = name,
            ["ril100"] = ril100,
            ["evaNumber"] = evaNumber,
            ["displayName"] = string.IsNullOrWhiteSpace(ril100)
                ? $"{name} ({evaNumber})"
                : $"{name} [{ril100}]"
        });
    }

    return stations;
}

static Dictionary<string, string> LoadEvaToRl100()
{
    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var path = Path.Combine(AppContext.BaseDirectory, "Data", "eva2rl100.ris");
    if (!File.Exists(path))
    {
        return map;
    }

    foreach (var line in File.ReadLines(path))
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
        {
            continue;
        }

        var parts = line.Split(',', 3);
        if (parts.Length < 2)
        {
            continue;
        }

        var eva = parts[0].Trim();
        var rl100 = parts[1].Trim();
        if (!string.IsNullOrWhiteSpace(eva) && !string.IsNullOrWhiteSpace(rl100))
        {
            map[eva] = rl100;
        }
    }

    return map;
}

static string? LookupRl100(string? evaNumber, IReadOnlyDictionary<string, string> evaToRl100)
{
    if (string.IsNullOrWhiteSpace(evaNumber))
    {
        return null;
    }

    var normalized = evaNumber.Trim();
    if (evaToRl100.TryGetValue(normalized, out var rl100))
    {
        return rl100;
    }

    if (long.TryParse(normalized, out var numericEva))
    {
        var padded = numericEva.ToString("D7");
        if (evaToRl100.TryGetValue(padded, out rl100))
        {
            return rl100;
        }
    }

    return null;
}

static IEnumerable<JsonNode?> ExtractStationCandidates(JsonNode? json)
{
    if (json is JsonArray directArray)
    {
        return directArray;
    }

    var array = json?["stopPlaces"]?.AsArray()
        ?? json?["stations"]?.AsArray()
        ?? json?["results"]?.AsArray()
        ?? json?["items"]?.AsArray();

    return array ?? [];
}

static string? ExtractRil100(JsonNode? station)
{
    var direct = GetString(station, "ril100")
        ?? GetString(station, "rilIdentifier")
        ?? GetString(station, "ril100Identifier");

    if (!string.IsNullOrWhiteSpace(direct))
    {
        return direct;
    }

    var identifiers = station?["ril100Identifiers"]?.AsArray();
    var fromIdentifier = identifiers?
        .Select(item => GetString(item, "rilIdentifier") ?? GetString(item, "ril100"))
        .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    if (!string.IsNullOrWhiteSpace(fromIdentifier))
    {
        return fromIdentifier;
    }

    var keys = station?["keys"]?.AsArray();
    return keys?
        .Select(item => GetString(item, "value") ?? GetString(item, "key"))
        .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

static List<StopRow> BuildStops(JsonArray? events)
{
    var stops = new List<StopRow>();
    if (events is null)
    {
        return stops;
    }

    foreach (var item in events)
    {
        var station = StopName(item?["stopPlace"]);
        if (string.IsNullOrWhiteSpace(station))
        {
            continue;
        }

        var type = GetString(item, "type") ?? "";
        var time = FormatTime(GetString(item, "time") ?? GetString(item, "timeSchedule"));
        var platform = GetString(item, "platform") ?? GetString(item, "platformSchedule") ?? "";
        var last = stops.LastOrDefault();

        if (last is not null && string.Equals(last.Station, station, StringComparison.OrdinalIgnoreCase))
        {
            if (type.Equals("ARRIVAL", StringComparison.OrdinalIgnoreCase))
            {
                last.Arrival = time;
            }
            else if (type.Equals("DEPARTURE", StringComparison.OrdinalIgnoreCase))
            {
                last.Departure = time;
            }

            if (string.IsNullOrWhiteSpace(last.Platform))
            {
                last.Platform = platform;
            }

            continue;
        }

        var row = new StopRow
        {
            Station = station,
            Platform = platform
        };

        if (type.Equals("ARRIVAL", StringComparison.OrdinalIgnoreCase))
        {
            row.Arrival = time;
        }
        else
        {
            row.Departure = time;
        }

        stops.Add(row);
    }

    return stops;
}

static string BuildLine(JsonNode? transport, JsonNode? info, int fallbackNumber)
{
    var description = GetString(transport, "journeyDescription");
    if (!string.IsNullOrWhiteSpace(description))
    {
        return description;
    }

    var category = GetString(transport, "category") ?? GetString(info, "category") ?? "Zug";
    var number = GetString(transport, "journeyNumber") ?? GetString(info, "headerJourneyNumber") ?? (fallbackNumber > 0 ? fallbackNumber.ToString() : "");
    return string.IsNullOrWhiteSpace(number) ? category : $"{category} {number}";
}

static string? StopName(JsonNode? stopPlace)
{
    return GetString(stopPlace, "name")
        ?? GetString(stopPlace, "stationName")
        ?? GetString(stopPlace, "nameLong")
        ?? GetString(stopPlace, "nameShort")
        ?? GetString(stopPlace?["names"]?["DE"], "nameLong")
        ?? GetString(stopPlace?["names"]?["DE"], "name")
        ?? GetString(stopPlace?["names"]?["DE"], "nameShort");
}

static string FormatTime(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return "";
    }

    if (DateTimeOffset.TryParse(value, out var dateTime))
    {
        return dateTime.ToLocalTime().ToString("HH:mm");
    }

    return value;
}

static string BuildDuration(List<StopRow> stops)
{
    var start = stops.FirstOrDefault()?.Departure;
    var end = stops.LastOrDefault()?.Arrival;
    if (string.IsNullOrWhiteSpace(start) || string.IsNullOrWhiteSpace(end))
    {
        return "";
    }

    return $"{start} - {end}";
}

static string? GetString(JsonNode? node, string propertyName)
{
    var value = node?[propertyName];
    if (value is null)
    {
        return null;
    }

    return value.GetValueKind() switch
    {
        JsonValueKind.String => value.GetValue<string>(),
        JsonValueKind.Number => value.ToJsonString(),
        _ => null
    };
}

static int? GetInt(JsonNode? node, string propertyName)
{
    var value = node?[propertyName];
    if (value is null)
    {
        return null;
    }

    return value.GetValueKind() == JsonValueKind.Number && value.AsValue().TryGetValue<int>(out var number)
        ? number
        : null;
}

static void ApplyAuth(HttpRequestMessage request, RisApiOptions options)
{
    if (!string.IsNullOrWhiteSpace(options.BearerToken))
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.BearerToken);
    }

    if (!string.IsNullOrWhiteSpace(options.ApiKeyHeaderName) && !string.IsNullOrWhiteSpace(options.ApiKey))
    {
        request.Headers.TryAddWithoutValidation(options.ApiKeyHeaderName, options.ApiKey);
    }

    if (!string.IsNullOrWhiteSpace(options.ClientIdHeaderName) && !string.IsNullOrWhiteSpace(options.ClientId))
    {
        request.Headers.TryAddWithoutValidation(options.ClientIdHeaderName, options.ClientId);
    }

    if (!string.IsNullOrWhiteSpace(options.ClientSecretHeaderName) && !string.IsNullOrWhiteSpace(options.ClientSecret))
    {
        request.Headers.TryAddWithoutValidation(options.ClientSecretHeaderName, options.ClientSecret);
    }
}

static void ApplyForwardHeaders(HttpRequestMessage request, HttpContext context)
{
    if (context.Request.Headers.TryGetValue("Accept", out var accept))
    {
        request.Headers.TryAddWithoutValidation("Accept", accept.ToArray());
    }

    request.Headers.UserAgent.ParseAdd("RIS-Communicator-LocalProxy/1.0");
}

public sealed class RisApiOptions
{
    public string? StationsBaseUrl { get; set; }
    public string? JourneysBaseUrl { get; set; }
    public string? AuthMode { get; set; }
    public string? BearerToken { get; set; }
    public string? ApiKeyHeaderName { get; set; }
    public string? ApiKey { get; set; }
    public string? ClientIdHeaderName { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecretHeaderName { get; set; }
    public string? ClientSecret { get; set; }

    public bool HasAnyAuth()
    {
        return !string.IsNullOrWhiteSpace(BearerToken)
            || (!string.IsNullOrWhiteSpace(ApiKeyHeaderName) && !string.IsNullOrWhiteSpace(ApiKey))
            || (!string.IsNullOrWhiteSpace(ClientIdHeaderName) && !string.IsNullOrWhiteSpace(ClientId));
    }
}

public sealed class RisInfoplattformOptions
{
    public string LoginUrl { get; set; } = "https://ris-info.bahn.de/#/login";
    public string? ChromePath { get; set; }
    public bool Headless { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 90;
}

public sealed class RisInfoplattformImportRequest
{
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string[]? TrainNumbers { get; set; }
    public string[]? AdministrationIds { get; set; }
}

public sealed record RisInfoplattformLoginResult(
    bool IsSupported,
    bool TokenFound,
    bool IsAuthoritativeFailure,
    int? StatusCode,
    int? ApiStatusCode,
    string? Message,
    object? ErrorDetails,
    string[] Endpoints,
    string[] TestedDataEndpoints,
    JsonArray Trains);

public sealed record RisDataProbeResult(JsonNode? Json, string? Url);

public sealed record BrowserRisImportResult(JsonArray Trains, string[] TestedEndpoints);

public sealed record BrowserAuthAttempt(string Label, string AuthMode, string Token);

public sealed record TokenCandidate(string Source, string Value);

public sealed record RisJsonResponse(JsonNode? Json, IResult? Error);

public sealed class StopRow
{
    public string Station { get; set; } = "";
    public string Arrival { get; set; } = "";
    public string Departure { get; set; } = "";
    public string Platform { get; set; } = "";
}

public sealed class ProxyResponseResult(byte[] body, string contentType, int statusCode) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = contentType;
        await httpContext.Response.Body.WriteAsync(body);
    }
}
