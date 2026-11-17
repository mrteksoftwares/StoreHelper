using System.Text;

namespace StoreHelper.Core;

public static class TextTools
{
    /// <summary>Türkçe karakterleri ASCII'ye indirger (ASCII-only terazi ekranları için). İ→I, ı→i.</summary>
    public static string FoldToAscii(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            switch (c)
            {
                case 'ç': sb.Append('c'); break;
                case 'Ç': sb.Append('C'); break;
                case 'ğ': sb.Append('g'); break;
                case 'Ğ': sb.Append('G'); break;
                case 'ı': sb.Append('i'); break;
                case 'İ': sb.Append('I'); break;
                case 'ö': sb.Append('o'); break;
                case 'Ö': sb.Append('O'); break;
                case 'ş': sb.Append('s'); break;
                case 'Ş': sb.Append('S'); break;
                case 'ü': sb.Append('u'); break;
                case 'Ü': sb.Append('U'); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Arama için: küçük harf + Türkçe karakter duyarsız ("Çay" == "cay" == "ÇAY").</summary>
    public static string SearchKey(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return FoldToAscii(text.Replace('İ', 'i').Replace('I', 'i')).ToLowerInvariant();
    }
}
