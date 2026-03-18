using System.Xml.Linq;

namespace Vibe.Office.Reporting.Rdl;

internal static class ReportRdlNamespaces
{
    public static readonly XNamespace Rdl2008 = "http://schemas.microsoft.com/sqlserver/reporting/2008/01/reportdefinition";
    public static readonly XNamespace Rdl2010 = "http://schemas.microsoft.com/sqlserver/reporting/2010/01/reportdefinition";
    public static readonly XNamespace Rdl2016 = "http://schemas.microsoft.com/sqlserver/reporting/2016/01/reportdefinition";
    public static readonly XNamespace DefaultFont2016 = "http://schemas.microsoft.com/sqlserver/reporting/2016/01/reportdefinition/defaultfontfamily";
    public static readonly XNamespace Designer = "http://schemas.microsoft.com/SQLServer/reporting/reportdesigner";

    public static XNamespace GetMainNamespace(ReportRdlVersion version)
    {
        return version switch
        {
            ReportRdlVersion.Rdl2008 => Rdl2008,
            ReportRdlVersion.Rdl2010 => Rdl2010,
            _ => Rdl2016
        };
    }

    public static bool TryGetVersion(XNamespace xmlNamespace, out ReportRdlVersion version)
    {
        if (xmlNamespace == Rdl2008)
        {
            version = ReportRdlVersion.Rdl2008;
            return true;
        }

        if (xmlNamespace == Rdl2010)
        {
            version = ReportRdlVersion.Rdl2010;
            return true;
        }

        if (xmlNamespace == Rdl2016)
        {
            version = ReportRdlVersion.Rdl2016;
            return true;
        }

        version = default;
        return false;
    }

    public static XNamespace? GetDefaultFontNamespace(ReportRdlVersion version)
    {
        return version switch
        {
            ReportRdlVersion.Rdl2016 => DefaultFont2016,
            _ => null
        };
    }
}
