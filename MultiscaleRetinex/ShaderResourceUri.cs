namespace MultiscaleRetinex;

internal static class ShaderResourceUri
{
    public static Uri Get(string shaderName) => new($"pack://application:,,,/MultiscaleRetinex;component/Shaders/{shaderName}.cso", UriKind.Absolute);
}
