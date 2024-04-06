using System.IO;
using System.Reflection;

class Resources
{
    public string FE3FileURL;
    public string GetCookie;
    public string WUIDRequest;
    private readonly Assembly _assembly = Assembly.GetExecutingAssembly();

    private string ReadManifestResource(string name)
    {
        string result = null;
        using (StreamReader streamReader = new(_assembly.GetManifestResourceStream(name)))
            result = streamReader.ReadToEnd();
        return result;
    }

    public Resources()
    {
        FE3FileURL = ReadManifestResource("FE3FileUrl");
        GetCookie = ReadManifestResource("GetCookie");
        WUIDRequest = ReadManifestResource("WUIDRequest");
    }
}