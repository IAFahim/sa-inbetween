using System.IO;
using SA.Inbetween.Engine;

namespace Planted.WrongWay;

public static class WrongWay
{
    public static System.Type EngineType => typeof(NetworkInputs);

    public static bool ReadsFiles(string path) => File.Exists(path);
}
