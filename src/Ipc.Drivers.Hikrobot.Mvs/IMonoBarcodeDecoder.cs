namespace Ipc.Drivers.Hikrobot.Mvs;

internal interface IMonoBarcodeDecoder
{
    void Prepare();
    string[] Decode(byte[] pixels, int length, int width, int height);
    Ipc.Platform.RecognitionSymbol[] DecodeDetailed(byte[] pixels, int length, int width, int height) =>
        Decode(pixels, length, width, height).Select(code => new Ipc.Platform.RecognitionSymbol(code, "Unavailable", [], 0)).ToArray();
    void Release();
}
