namespace Ipc.Drivers.Hikrobot.Mvs;

internal interface IMonoBarcodeDecoder
{
    void Prepare();
    string[] Decode(byte[] pixels, int length, int width, int height);
    void Release();
}
