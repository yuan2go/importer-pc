namespace Ipc.Application.Contracts;

public interface ICameraDiscoveryProvider : IAsyncDisposable
{
    string DriverId { get; }
    bool SupportsEngine(string readerEngine);
    DiscoveredCamera[] Discover(string readerEngine);
    string ReadSerial(string ip, string readerEngine);
}
