using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KAONOMI.Agent.WebScraper.Collect.Scraping
{
    /// <summary>
    /// How a collected page is named: its document (id and name) and its files. A main page has one document per day
    /// (collected again the same day, it is overwritten); any other page has one document, overwritten each time.
    /// The files are in <c>&lt;root&gt;/&lt;collection plan&gt;/&lt;website&gt;/&lt;yyyyMMdd&gt;</c>.
    /// </summary>
    public static class PageNames
    {
        /// <summary>The day, as in the names: <c>yyyyMMdd</c>.</summary>
        public static string Day(DateTime date) => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        /// <summary>
        /// The id of the document: made of the collection plan, the URL (without its fragment and its last slash, case
        /// ignored) and, for a main page, the day. The same page collected again gives the same id, so it is overwritten.
        /// </summary>
        public static string Id(string collectionPlanId, Uri url, bool isMainPage, string day)
        {
            var key = $"{collectionPlanId}|{Normalize(url).ToLowerInvariant()}|{(isMainPage ? day : string.Empty)}";
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32].ToLowerInvariant();
        }

        /// <summary>The name of the page: its title (its address when it has none), followed by the day for a main page.</summary>
        public static string Name(string title, Uri url, bool isMainPage, string day)
        {
            var name = string.IsNullOrWhiteSpace(title) ? url.Host + url.AbsolutePath.TrimEnd('/') : title.Trim();
            return isMainPage ? $"{name} {day}" : name;
        }

        /// <summary>The URL without its fragment and its last slash: two spellings of the same page are the same page.</summary>
        public static string Normalize(Uri url) => url.GetLeftPart(UriPartial.Query).TrimEnd('/');

        /// <summary>The folder of the files of a page: <c>&lt;root&gt;/&lt;collection plan&gt;/&lt;website&gt;/&lt;yyyyMMdd&gt;</c>.</summary>
        public static string Folder(string root, string collectionPlanName, string websiteName, Uri url, string day) =>
            Path.Combine(root, FolderName(collectionPlanName, "plan"), FolderName(websiteName, url.Host), day);

        /// <summary>
        /// A name of folder that reads as <paramref name="name"/>: its characters that a file system refuses (and the
        /// dots at its ends) replaced; <paramref name="fallback"/> when nothing is left.
        /// </summary>
        public static string FolderName(string? name, string fallback)
        {
            var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }).ToHashSet();
            var clean = new string((name ?? string.Empty).Select(c => invalid.Contains(c) || char.IsControl(c) ? '-' : c).ToArray()).Trim().Trim('.', '-').Trim();
            if (clean.Length > 80)
                clean = clean[..80].Trim();
            return clean.Length > 0 ? clean : FolderName(fallback, "site");
        }

        /// <summary>
        /// What the names of the files of a page start with: letters and digits of its title, then the first eight
        /// characters of its id (two pages of the same title do not share their files).
        /// </summary>
        public static string FilePrefix(string title, string id) => $"{Slug(title, "page")}-{id[..Math.Min(8, id.Length)]}";

        /// <summary>The letters and digits of <paramref name="text"/> (accents taken off), in lower case, <c>-</c> between the words.</summary>
        public static string Slug(string? text, string fallback, int most = 60)
        {
            var slug = new StringBuilder();
            foreach (var ch in (text ?? string.Empty).Normalize(NormalizationForm.FormD))
            {
                if (char.IsLetterOrDigit(ch) && ch < 128)
                    slug.Append(char.ToLowerInvariant(ch));
                else if (slug.Length > 0 && slug[^1] != '-' && CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                    slug.Append('-');
            }
            var result = slug.ToString().Trim('-');
            if (result.Length > most)
                result = result[..most].Trim('-');
            return result.Length == 0 ? fallback : result;
        }

        /// <summary>
        /// Removes the files of a page already in <paramref name="folder"/> (its rendering and its pictures, from an
        /// earlier collection the same day): the page collected again replaces them. Its files are those whose name
        /// holds the first characters of its id.
        /// </summary>
        public static void RemoveFiles(string folder, string id)
        {
            if (!Directory.Exists(folder))
                return;
            var mark = "-" + id[..Math.Min(8, id.Length)];
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                var name = Path.GetFileName(file);
                if (name.Contains(mark + ".", StringComparison.Ordinal) || name.Contains(mark + "-", StringComparison.Ordinal))
                    File.Delete(file);
            }
        }
    }
}
