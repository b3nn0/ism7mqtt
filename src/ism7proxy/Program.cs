﻿using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ism7ssl;
using Mono.Options;

namespace ism7proxy
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var showHelp = false;
            string ip = null;
            int port = 9092;
            var options = new OptionSet
            {
                {"i|ipAddress=", "Wolf Hostname or IP address", x => ip = x},
                {"p|port=", "Wolf port (default 9092, older Firmware uses 9091)", (int x) => port = x},
                {"h|help", "show help", x => showHelp = x != null},
            };
            try
            {
                if (options.Parse(args).Count > 0)
                {
                    showHelp = true;
                }
            }
            catch (OptionException ex)
            {
                Console.Error.Write("ism7proxy: ");
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine("Try 'ism7proxy --help' for more information");
                return;
            }
            if (showHelp || ip is null)
            {
                options.WriteOptionDescriptions(Console.Out);
                return;
            }
            try
            {
                var server = new TcpListener(IPAddress.Any, 9092);
                using (var cts = new CancellationTokenSource())
                {
                    Console.CancelKeyPress += (s, e) =>
                    {
                        e.Cancel = true;
                        cts.Cancel();
                    };
                    server.Start();
                    while (!cts.IsCancellationRequested)
                    {
                        using(var incoming = await server.AcceptTcpClientAsync(cts.Token))
                        using (var outgoing = new TcpClient(ip, port))
                        using(var sslServer = new SslStream(incoming.GetStream(), true, (a, b, c, d) => true))
                        using (var sslClient = new Ism7SslStream(outgoing.Client))
                        {
                            var certificate = Ism7SslStream.Certificate;
                            await sslServer.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                            {
                                ServerCertificate = certificate,
                                ClientCertificateRequired = true
                            }, cts.Token);

                            await sslClient.AuthenticateAsClientAsync(cts.Token);
                            var toServer = CopyAsync("< ", sslClient, sslServer, cts.Token);
                            var toClient = CopyAsync("> ", sslServer, sslClient, cts.Token);
                            await Task.WhenAny(toServer, toClient);
                        }
                    }

                }
            }
            catch(OperationCanceledException)
            {}
        }

        private static async Task CopyAsync(string prefix, Stream source, Stream target, CancellationToken cancellationToken)
        {
            var buf = new byte[4096];
            while (!cancellationToken.IsCancellationRequested)
            {
                var count = await source.ReadAsync(buf, cancellationToken);
                if (count == 0) return;
                var start = buf.AsSpan(0, count).IndexOf((byte) 0x3c);
                if (start < 0)
                    Console.WriteLine(prefix + '(' + HexEncode(buf.AsSpan(0, count)) + ")");
                else if (start == 0)
                    Console.WriteLine(prefix + Encoding.UTF8.GetString(buf.AsSpan(0, count)));
                else
                    Console.WriteLine(prefix + '(' + HexEncode(buf.AsSpan(0, start)) + "):" + Encoding.UTF8.GetString(buf.AsSpan(start, count - start)));
                await target.WriteAsync(buf.AsMemory(0, count), cancellationToken);
            }
        }

        private static string HexEncode(ReadOnlySpan<byte> data)
        {
            StringBuilder s = new StringBuilder(data.Length*2);
            foreach (byte b in data)
                s.Append(b.ToString("x2"));
            return s.ToString();
        }
    }
}
