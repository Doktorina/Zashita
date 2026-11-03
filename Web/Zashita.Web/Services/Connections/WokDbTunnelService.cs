using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace Zashita.Web.Services.Connections;

/// <summary>
/// Сам поднимает SSH-туннель к PostgreSQL WOK (без bat-файлов).
/// </summary>
public sealed class WokDbTunnelService : IAsyncDisposable
{
    private readonly WokUsersOptions _options;
    private readonly ILogger<WokDbTunnelService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Process? _ssh;

    public WokDbTunnelService(IOptions<WokUsersOptions> options, ILogger<WokDbTunnelService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> EnsureAsync(CancellationToken ct = default)
    {
        if (!_options.AutoTunnel)
        {
            return IsListening(_options.TunnelLocalPort);
        }

        if (IsListening(_options.TunnelLocalPort))
        {
            return true;
        }

        await _lock.WaitAsync(ct);
        try
        {
            if (IsListening(_options.TunnelLocalPort))
            {
                return true;
            }

            if (!StartSshProcess())
            {
                return false;
            }

            var deadline = DateTime.UtcNow.AddSeconds(25);
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                if (IsListening(_options.TunnelLocalPort))
                {
                    _logger.LogInformation(
                        "WOK DB tunnel ready on 127.0.0.1:{Port}",
                        _options.TunnelLocalPort);
                    return true;
                }

                if (_ssh is { HasExited: true })
                {
                    _logger.LogWarning("SSH tunnel process exited with code {Code}", _ssh.ExitCode);
                    return false;
                }

                await Task.Delay(400, ct);
            }

            _logger.LogWarning("SSH tunnel: port {Port} did not open in time", _options.TunnelLocalPort);
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    private bool StartSshProcess()
    {
        try
        {
            if (_ssh is { HasExited: false })
            {
                try { _ssh.Kill(entireProcessTree: true); } catch { /* ignore */ }
                _ssh.Dispose();
                _ssh = null;
            }

            var key = Expand(_options.SshKeyPath);
            if (string.IsNullOrWhiteSpace(key) || !File.Exists(key))
            {
                _logger.LogWarning("SSH key not found: {Key}", key);
                return false;
            }

            var local = _options.TunnelLocalPort > 0 ? _options.TunnelLocalPort : 5433;
            var remotePort = _options.TunnelRemotePort > 0 ? _options.TunnelRemotePort : 5432;
            var sshPort = _options.SshPort > 0 ? _options.SshPort : 22;
            var host = (_options.SshHost ?? string.Empty).Trim();
            var user = (_options.SshUser ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(user))
            {
                _logger.LogWarning("Wok SSH host/user not configured; tunnel skipped.");
                return false;
            }

            var args =
                $"-i \"{key}\" -p {sshPort} -N " +
                "-o BatchMode=yes -o ExitOnForwardFailure=yes -o ServerAliveInterval=30 " +
                "-o StrictHostKeyChecking=accept-new " +
                $"-L {local}:127.0.0.1:{remotePort} {user}@{host}";

            var psi = new ProcessStartInfo
            {
                FileName = "ssh",
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            _ssh = Process.Start(psi);
            if (_ssh is null)
            {
                _logger.LogWarning("Failed to start ssh process");
                return false;
            }

            _logger.LogInformation("Started SSH tunnel to {User}@{Host}:{SshPort}", user, host, sshPort);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cannot start SSH tunnel");
            return false;
        }
    }

    private static string Expand(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".ssh",
                "id_ed25519");
        }

        return Environment.ExpandEnvironmentVariables(path.Trim());
    }

    private static bool IsListening(int port)
    {
        if (port <= 0)
        {
            port = 5433;
        }

        try
        {
            using var client = new TcpClient();
            var result = client.BeginConnect("127.0.0.1", port, null, null);
            var ok = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(600));
            if (!ok)
            {
                return false;
            }

            client.EndConnect(result);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (_ssh is { HasExited: false })
            {
                _ssh.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // ignore
        }

        _ssh?.Dispose();
        _lock.Dispose();
        return ValueTask.CompletedTask;
    }
}
