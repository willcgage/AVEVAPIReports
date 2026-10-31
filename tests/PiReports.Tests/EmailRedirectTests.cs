using System.Net;
using System.Net.Sockets;
using System.Text;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PiReports.Core;
using PiReports.Core.Data;
using PiReports.Core.Delivery;
using PiReports.Core.Model;

namespace PiReports.Tests;

public class EmailRedirectTests
{
    /// <summary>Just enough SMTP to accept one message and record the envelope and data.</summary>
    private sealed class FakeSmtpServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

        public FakeSmtpServer()
        {
            _listener.Start();
            Received = Task.Run(AcceptOneAsync);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public List<string> RcptTo { get; } = [];

        public Task<string> Received { get; }

        private async Task<string> AcceptOneAsync()
        {
            using var client = await _listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
            await writer.WriteLineAsync("220 fake");
            var data = new StringBuilder();
            while (await reader.ReadLineAsync() is { } line)
            {
                var verb = line.Split(' ')[0].ToUpperInvariant();
                switch (verb)
                {
                    case "EHLO" or "HELO": await writer.WriteLineAsync("250 fake"); break;
                    case "RCPT": RcptTo.Add(line[(line.IndexOf('<') + 1)..line.IndexOf('>')]); await writer.WriteLineAsync("250 ok"); break;
                    case "DATA":
                        await writer.WriteLineAsync("354 go");
                        while (await reader.ReadLineAsync() is { } d && d != ".")
                        {
                            data.AppendLine(d);
                        }

                        await writer.WriteLineAsync("250 queued");
                        break;
                    case "QUIT": await writer.WriteLineAsync("221 bye"); return data.ToString();
                    default: await writer.WriteLineAsync("250 ok"); break;
                }
            }

            return data.ToString();
        }

        public void Dispose() => _listener.Stop();
    }

    private static async Task<(FakeSmtpServer Server, EmailSender Sender)> SetupAsync(PiReportsOptions options)
    {
        var server = new FakeSmtpServer();
        var db = new PiReportsDbContext(new DbContextOptionsBuilder<PiReportsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.SmtpSettings.Add(new SmtpSettings { Host = "127.0.0.1", Port = server.Port, Security = SecureSocketOptions.None, FromAddress = "pi@corp.local" });
        await db.SaveChangesAsync();
        return (server, new EmailSender(db, new EphemeralDataProtectionProvider(), Options.Create(options)));
    }

    [Fact]
    public async Task Test_install_sends_only_to_the_redirect_address_with_a_tagged_subject()
    {
        var (server, sender) = await SetupAsync(new PiReportsOptions { EnvironmentLabel = "TEST", EmailRedirectTo = "will@corp.local" });
        using var _ = server;

        var sentTo = await sender.SendAsync(["plant-manager@corp.local", "ops@corp.local"], "Unit 3", "<p>body</p>", null, CancellationToken.None);
        var data = await server.Received;

        Assert.Equal(["will@corp.local"], sentTo);
        Assert.Equal(["will@corp.local"], server.RcptTo);
        Assert.Contains("Subject: [TEST] Unit 3", data);
        Assert.Contains("plant-manager@corp.local, ops@corp.local", data);
    }

    [Fact]
    public async Task Production_install_sends_to_the_real_recipients()
    {
        var (server, sender) = await SetupAsync(new PiReportsOptions());
        using var _ = server;

        await sender.SendAsync(["ops@corp.local"], "Unit 3", "<p>body</p>", null, CancellationToken.None);
        var data = await server.Received;

        Assert.Equal(["ops@corp.local"], server.RcptTo);
        Assert.Contains("Subject: Unit 3", data);
    }
}
