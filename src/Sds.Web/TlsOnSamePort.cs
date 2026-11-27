using System.IO.Pipelines;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;

namespace Sds.Web;

/// <summary>
/// Lets one port speak both HTTP and HTTPS, so http://host:8351 and https://host:8351 both
/// reach the console. Kestrel's own UseHttps makes a port HTTPS only.
///
/// Steps, for every new connection:
///   1. Look at the first byte without consuming it. A TLS connection always starts with a
///      handshake record, whose first byte is 0x16; an HTTP request starts with a letter.
///   2. Plain HTTP: hand the connection to Kestrel unchanged.
///   3. TLS: run the server side of the handshake (TLS 1.2 or 1.3, HTTP/1.1) over the
///      connection, then hand Kestrel the decrypted stream, flagged as TLS so the request's
///      scheme is "https". A failed handshake (for example a browser that refuses the
///      certificate) just closes the connection.
/// </summary>
internal static class TlsOnSamePort
{
    /// <summary>First byte of a TLS record of type "handshake".</summary>
    private const byte TlsHandshakeRecord = 0x16;

    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(10);

    public static Func<ConnectionDelegate, ConnectionDelegate> Middleware(X509Certificate2 certificate)
    {
        var certificateContext = SslStreamCertificateContext.Create(certificate, additionalCertificates: null);

        return next => async connection =>
        {
            // Step 1: peek. AdvanceTo(start) marks nothing as consumed, so Kestrel (or the
            // handshake) reads the same bytes again.
            var input = connection.Transport.Input;
            var read = await input.ReadAsync(connection.ConnectionClosed);
            var isTls = !read.Buffer.IsEmpty && read.Buffer.FirstSpan[0] == TlsHandshakeRecord;
            input.AdvanceTo(read.Buffer.Start, read.Buffer.Start);

            // Step 2
            if (!isTls)
            {
                await next(connection);
                return;
            }

            // Step 3
            var original = connection.Transport;
            await using var ssl = new SslStream(new DuplexPipeStream(original), leaveInnerStreamOpen: true);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(connection.ConnectionClosed);
                timeout.CancelAfter(HandshakeTimeout);
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificateContext = certificateContext,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    ApplicationProtocols = [SslApplicationProtocol.Http11],
                    ClientCertificateRequired = false,
                }, timeout.Token);
            }
            catch (Exception ex) when (ex is AuthenticationException or IOException or OperationCanceledException)
            {
                return;
            }

            connection.Features.Set<ITlsConnectionFeature>(new TlsConnectionFeature());
            connection.Transport = new DuplexPipe(PipeReader.Create(ssl, new StreamPipeReaderOptions(leaveOpen: true)), PipeWriter.Create(ssl, new StreamPipeWriterOptions(leaveOpen: true)));
            try
            {
                await next(connection);
            }
            finally
            {
                connection.Transport = original;
            }
        };
    }

    /// <summary>Marks the connection as TLS; Kestrel then reports the request scheme as https.</summary>
    private sealed class TlsConnectionFeature : ITlsConnectionFeature
    {
        public X509Certificate2? ClientCertificate { get; set; }

        public Task<X509Certificate2?> GetClientCertificateAsync(CancellationToken cancellationToken) =>
            Task.FromResult<X509Certificate2?>(null);
    }

    private sealed class DuplexPipe(PipeReader input, PipeWriter output) : IDuplexPipe
    {
        public PipeReader Input { get; } = input;
        public PipeWriter Output { get; } = output;
    }

    /// <summary>The raw connection as a Stream, for SslStream.</summary>
    private sealed class DuplexPipeStream(IDuplexPipe pipe) : Stream
    {
        private readonly Stream _input = pipe.Input.AsStream(leaveOpen: true);
        private readonly Stream _output = pipe.Output.AsStream(leaveOpen: true);

        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => _input.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _input.ReadAsync(buffer, cancellationToken);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _input.ReadAsync(buffer, offset, count, cancellationToken);

        public override void Write(byte[] buffer, int offset, int count) => _output.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => _output.WriteAsync(buffer, cancellationToken);
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _output.WriteAsync(buffer, offset, count, cancellationToken);
        public override void Flush() => _output.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => _output.FlushAsync(cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
