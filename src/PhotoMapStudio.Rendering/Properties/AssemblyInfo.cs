using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("PhotoMapStudio.Rendering.Tests")]
// MercatorProjection と Core.Geo.WebMercator の一致を、両方を参照できる Core.Tests で検証する
[assembly: InternalsVisibleTo("PhotoMapStudio.Core.Tests")]
