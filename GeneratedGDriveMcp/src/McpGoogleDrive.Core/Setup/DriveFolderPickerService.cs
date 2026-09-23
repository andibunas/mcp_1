using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using McpGoogleDrive.Core.Configuration;
using Microsoft.Extensions.Options;

namespace McpGoogleDrive.Core.Setup;

/// <summary>
/// Interactive, browser-based folder grant flow: serves a one-off local HTML page embedding
/// the Google Picker widget, lets the user visually select Drive folders, and captures the
/// resulting folder IDs — no manual copy-pasting of folder IDs out of Drive URLs.
///
/// Requires <see cref="GoogleAuthOptions.PickerApiKey"/> to be configured (see docs/setup.md).
/// For headless environments (no browser, e.g. an AWS deployment), skip this and configure
/// <see cref="DriveAccessOptions.AllowedFolderIds"/> manually instead.
/// </summary>
public sealed class DriveFolderPickerService(IOptions<GoogleAuthOptions> authOptions)
{
    private readonly GoogleAuthOptions _authOptions = authOptions.Value;

    public async Task<IReadOnlyList<PickedFolder>> PickFoldersAsync(
        string accessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_authOptions.PickerApiKey))
        {
            throw new InvalidOperationException(
                "GoogleAuth:PickerApiKey is not configured. See docs/setup.md for how to create a " +
                "Picker API key, or use manual folder-ID entry instead.");
        }

        using var listener = new HttpListener();
        var port = GetFreeTcpPort();
        var prefix = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(prefix);
        listener.Start();

        var tcs = new TaskCompletionSource<IReadOnlyList<PickedFolder>>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));

        var serverTask = HandleRequestsAsync(listener, accessToken, tcs, cancellationToken);

        OpenBrowser(prefix);

        try
        {
            return await tcs.Task;
        }
        finally
        {
            listener.Stop();
            await Task.WhenAny(serverTask, Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None));
        }
    }

    private async Task HandleRequestsAsync(
        HttpListener listener,
        string accessToken,
        TaskCompletionSource<IReadOnlyList<PickedFolder>> tcs,
        CancellationToken cancellationToken)
    {
        try
        {
            while (listener.IsListening)
            {
                var context = await listener.GetContextAsync().WaitAsync(cancellationToken);
                var path = context.Request.Url?.AbsolutePath ?? "/";

                if (path == "/" && context.Request.HttpMethod == "GET")
                {
                    var html = BuildPickerHtml(accessToken, _authOptions.PickerApiKey);
                    await WriteResponseAsync(context, "text/html", html);
                }
                else if (path == "/picked" && context.Request.HttpMethod == "POST")
                {
                    using var reader = new StreamReader(context.Request.InputStream);
                    var body = await reader.ReadToEndAsync(cancellationToken);
                    var folders = JsonSerializer.Deserialize<List<PickedFolder>>(body) ?? [];
                    await WriteResponseAsync(context, "text/plain", "Folders selected — you can close this tab.");
                    tcs.TrySetResult(folders);
                    return;
                }
                else if (path == "/cancelled" && context.Request.HttpMethod == "POST")
                {
                    await WriteResponseAsync(context, "text/plain", "Cancelled — you can close this tab.");
                    tcs.TrySetException(new OperationCanceledException("Folder selection was cancelled in the browser."));
                    return;
                }
                else
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                }
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            tcs.TrySetException(ex);
        }
    }

    private static async Task WriteResponseAsync(HttpListenerContext context, string contentType, string body)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(body);
        context.Response.ContentType = contentType;
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.OutputStream.Close();
    }

    private static string BuildPickerHtml(string accessToken, string pickerApiKey) => $$"""
        <!DOCTYPE html>
        <html>
        <head><meta charset="utf-8"><title>Grant Drive folder access</title></head>
        <body>
          <p>Pick the Drive folders this MCP server may access, then close this tab.</p>
          <script src="https://apis.google.com/js/api.js"></script>
          <script>
            const accessToken = {{JsonSerializer.Serialize(accessToken)}};
            const developerKey = {{JsonSerializer.Serialize(pickerApiKey)}};

            function post(path, body) {
              return fetch(path, { method: "POST", body: body ?? "[]" });
            }

            function onPicked(data) {
              if (data.action === google.picker.Action.PICKED) {
                const folders = data.docs.map(d => ({ id: d.id, name: d.name }));
                post("/picked", JSON.stringify(folders));
              } else if (data.action === google.picker.Action.CANCEL) {
                post("/cancelled");
              }
            }

            function createPicker() {
              const view = new google.picker.DocsView(google.picker.ViewId.FOLDERS)
                .setSelectFolderEnabled(true)
                .setIncludeFolders(true);
              const picker = new google.picker.PickerBuilder()
                .addView(view)
                .setOAuthToken(accessToken)
                .setDeveloperKey(developerKey)
                .setCallback(onPicked)
                .build();
              picker.setVisible(true);
            }

            gapi.load("picker", createPicker);
          </script>
        </body>
        </html>
        """;

    private static void OpenBrowser(string url)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", url);
            }
            else if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
            }
            else
            {
                Process.Start("xdg-open", url);
            }
        }
        catch (Exception)
        {
            Console.WriteLine($"Open this URL in a browser to continue: {url}");
        }
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
