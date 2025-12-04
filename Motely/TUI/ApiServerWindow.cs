using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Motely.API;
using Terminal.Gui;

namespace Motely.TUI;

public class ApiServerWindow : Window
{
    private TextView _logView;
    private Label _statusLabel;
    private Label _urlLabel;
    private Label _tunnelLabel;
    private CleanButton _stopButton;
    private CleanButton _tunnelButton;
    private MotelyApiServer? _server;
    private CancellationTokenSource? _cts;
    private Process? _tunnelProcess;
    private bool _isRunning = false;
    private string _serverUrl = "";

    public ApiServerWindow(string host = "localhost", int port = 3141)
    {
        _serverUrl = $"http://{host}:{port}/";

        // Wide window to accommodate long cloudflare URLs
        Title = "API Server";
        X = Pos.Center();
        Y = Pos.Center();
        Width = Dim.Percent(85); // Use 85% of screen width
        Height = 22;
        CanFocus = true;
        SetScheme(BalatroTheme.Window);

        // Status row
        _statusLabel = new Label()
        {
            X = 1,
            Y = 1,
            Text = "Starting...",
        };
        _statusLabel.SetScheme(new Scheme()
        {
            Normal = new Attribute(BalatroTheme.Orange, BalatroTheme.ModalGrey),
        });
        Add(_statusLabel);

        // URL (clickable hint) - full width to show complete URL
        _urlLabel = new Label()
        {
            X = 1,
            Y = 2,
            Width = Dim.Fill() - 2,
            Text = _serverUrl,
        };
        _urlLabel.SetScheme(new Scheme()
        {
            Normal = new Attribute(BalatroTheme.Blue, BalatroTheme.ModalGrey),
        });
        _urlLabel.MouseClick += (s, e) =>
        {
            CopyToClipboard(_serverUrl);
            LogMessage($"[CLIPBOARD] Copied URL: {_serverUrl}");
            e.Handled = true;
        };
        Add(_urlLabel);

        // Tunnel status & button - full width to show complete URL
        _tunnelLabel = new Label()
        {
            X = 1,
            Y = 3,
            Width = Dim.Fill() - 2, // Full width minus margins
            Text = "",
        };
        _tunnelLabel.SetScheme(new Scheme()
        {
            Normal = new Attribute(BalatroTheme.Green, BalatroTheme.ModalGrey),
        });
        _tunnelLabel.MouseClick += (s, e) =>
        {
            if (!string.IsNullOrWhiteSpace(_tunnelLabel.Text.ToString()))
            {
                var urlText = _tunnelLabel.Text.ToString();
                if (urlText.StartsWith("https://"))
                {
                    CopyToClipboard(urlText);
                    LogMessage($"[CLIPBOARD] Copied URL: {urlText}");
                }
            }
            e.Handled = true;
        };
        Add(_tunnelLabel);

        _tunnelButton = new CleanButton()
        {
            X = Pos.AnchorEnd(18),
            Y = 1,
            Text = "Start Tunnel",
        };
        _tunnelButton.SetScheme(BalatroTheme.PurpleButton);
        _tunnelButton.Accept += (s, e) => StartTunnel();
        Add(_tunnelButton);

        // Endpoints panel
        var endpointsFrame = new FrameView()
        {
            X = 1,
            Y = 5,
            Width = Dim.Fill() - 2,
            Height = 5,
            Title = "Endpoints",
        };
        endpointsFrame.SetScheme(BalatroTheme.InnerPanel);
        Add(endpointsFrame);

        // Endpoint buttons (visual, show what's available)
        var searchEndpoint = new CleanButton()
        {
            X = 1,
            Y = 0,
            Text = "POST /search",
        };
        searchEndpoint.SetScheme(BalatroTheme.BlueButton);
        searchEndpoint.Accept += (s, e) => LogMessage("[INFO] /search - Search random seeds with filter");
        endpointsFrame.Add(searchEndpoint);

        var searchDesc = new Label()
        {
            X = Pos.Right(searchEndpoint) + 2,
            Y = 0,
            Text = "Search random seeds",
        };
        endpointsFrame.Add(searchDesc);

        var analyzeEndpoint = new CleanButton()
        {
            X = 1,
            Y = 2,
            Text = "POST /analyze",
        };
        analyzeEndpoint.SetScheme(BalatroTheme.GreenButton);
        analyzeEndpoint.Accept += (s, e) => LogMessage("[INFO] /analyze - Analyze a specific seed");
        endpointsFrame.Add(analyzeEndpoint);

        var analyzeDesc = new Label()
        {
            X = Pos.Right(analyzeEndpoint) + 2,
            Y = 2,
            Text = "Analyze specific seed",
        };
        endpointsFrame.Add(analyzeDesc);

        // Request log
        var logFrame = new FrameView()
        {
            X = 1,
            Y = 10,
            Width = Dim.Fill() - 2,
            Height = 8,
            Title = "Request Log",
        };
        logFrame.SetScheme(BalatroTheme.InnerPanel);
        Add(logFrame);

        _logView = new TextView()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = true,
            WordWrap = false,
            CanFocus = true,
        };
        _logView.SetScheme(new Scheme()
        {
            Normal = new Attribute(BalatroTheme.LightGrey, BalatroTheme.InnerPanelGrey),
            Focus = new Attribute(BalatroTheme.White, BalatroTheme.InnerPanelGrey),
        });
        logFrame.Add(_logView);

        // Stop Server button - red, above Back
        _stopButton = new CleanButton()
        {
            X = 1,
            Y = Pos.AnchorEnd(3),
            Text = "Stop Server Host",
            Width = Dim.Fill() - 2,
            TextAlignment = Alignment.Center,
        };
        _stopButton.SetScheme(BalatroTheme.RedButton);
        _stopButton.Accept += (s, e) => StopServerOnly();
        Add(_stopButton);

        // Back button - orange
        var backButton = new CleanButton()
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Text = "Back",
            Width = Dim.Fill() - 2,
            TextAlignment = Alignment.Center,
        };
        backButton.SetScheme(BalatroTheme.BackButton);
        backButton.Accept += (s, e) => StopAndClose();
        Add(backButton);

        // Keyboard shortcuts
        KeyDown += (sender, e) =>
        {
            if (e.KeyCode == KeyCode.Esc)
            {
                StopAndClose();
                e.Handled = true;
            }
        };

        // Start server automatically
        Task.Run(() => StartServerAsync(host, port));
    }

    private async Task StartServerAsync(string host, int port)
    {
        try
        {
            _isRunning = true;
            _cts = new CancellationTokenSource();

            _server = new MotelyApiServer(host, port, LogMessage);

            App?.Invoke(() =>
            {
                _statusLabel.Text = "Running";
                _statusLabel.SetScheme(new Scheme()
                {
                    Normal = new Attribute(BalatroTheme.Green, BalatroTheme.ModalGrey),
                });
            });

            LogMessage($"Server started on {_serverUrl}");
            LogMessage("Web UI available at same URL");

            await _server.StartAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            LogMessage($"[ERROR] {ex.Message}");
            App?.Invoke(() =>
            {
                _statusLabel.Text = "Failed";
                _statusLabel.SetScheme(new Scheme()
                {
                    Normal = new Attribute(BalatroTheme.Red, BalatroTheme.ModalGrey),
                });
            });
        }
        finally
        {
            _isRunning = false;
            App?.Invoke(() =>
            {
                _statusLabel.Text = "Stopped";
                _statusLabel.SetScheme(new Scheme()
                {
                    Normal = new Attribute(BalatroTheme.Gray, BalatroTheme.ModalGrey),
                });
                _stopButton.Visible = false; // Hide when server stops (back button remains)
            });
        }
    }

    private void StopServerOnly()
    {
        if (_isRunning)
        {
            LogMessage("Stopping server...");
            _cts?.Cancel();
            _server?.Stop();
            _stopButton.Enabled = false;
            _stopButton.Visible = false; // Hide when stopped
        }
    }

    private void StopAndClose()
    {
        StopServerOnly();
        StopTunnel();
        App?.RequestStop();
    }

    private void StartTunnel()
    {
        if (_tunnelProcess != null)
        {
            LogMessage("[TUNNEL] Already running");
            return;
        }

        _tunnelButton.Text = "Starting...";
        _tunnelButton.Enabled = false;

        Task.Run(() =>
        {
            try
            {
                // Use .NET HTTP hosting instead of external cloudflared dependency
                LogMessage("[TUNNEL] External tunneling not supported - use local network access or port forwarding");
                throw new NotSupportedException("External tunneling removed - use local network or manual port forwarding");
            }
            catch (Exception ex)
            {
                App?.Invoke(() =>
                {
                    _tunnelButton.Text = "Start Tunnel";
                    _tunnelButton.Enabled = true;
                    _tunnelLabel.Text = "";
                });
                LogMessage($"[TUNNEL] Error: {ex.Message}");
                LogMessage("[TUNNEL] Use local network access or manual port forwarding instead");
                _tunnelProcess = null;
            }
        });
    }

    private void StopTunnel()
    {
        if (_tunnelProcess != null && !_tunnelProcess.HasExited)
        {
            try
            {
                _tunnelProcess.Kill();
                _tunnelProcess.Dispose();
                LogMessage("[TUNNEL] Stopped");
            }
            catch { }
        }
        _tunnelProcess = null;
        App?.Invoke(() =>
        {
            _tunnelLabel.Text = "";
            _tunnelButton.Text = "Start Tunnel";
            _tunnelButton.Enabled = true;
            _tunnelButton.SetScheme(BalatroTheme.PurpleButton);
        });
    }

    private void LogMessage(string message)
    {
        try
        {
            App?.Invoke(() =>
            {
                if (_logView != null)
                {
                    _logView.Text += message + "\n";
                    _logView.MoveEnd();
                }
            });
        }
        catch (ObjectDisposedException)
        {
            // Window closed while logging - ignore
        }
    }

    // Removed FindCloudflared() - external tunnel dependencies removed for cross-platform compatibility

    private void CopyToClipboard(string text)
    {
        try
        {
            // Use platform-specific commands directly (Terminal.Gui clipboard may truncate)
            if (OperatingSystem.IsWindows())
            {
                var psi = new System.Diagnostics.ProcessStartInfo("clip")
                {
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                var proc = System.Diagnostics.Process.Start(psi);
                proc?.StandardInput.Write(text);
                proc?.StandardInput.Close();
                proc?.WaitForExit();
            }
            else if (OperatingSystem.IsMacOS())
            {
                var psi = new System.Diagnostics.ProcessStartInfo("pbcopy")
                {
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                var proc = System.Diagnostics.Process.Start(psi);
                proc?.StandardInput.Write(text);
                proc?.StandardInput.Close();
                proc?.WaitForExit();
            }
        }
        catch
        {
            // Ignore clipboard errors
        }
    }
}
