namespace HuggingFaceHubSharp.Model
{
    public class HttpResponse
    {
        public string status_code { get; set; }
        public Dictionary<string, string> headers { get; set; }

        public Dictionary<string, Dictionary<string, string>> links
        {
            get
            {
                var header = this.headers.GetValueOrDefault("link");

                if (header == null)
                {
                    return new Dictionary<string, Dictionary<string, string>>();
                }

                var result = new Dictionary<string, Dictionary<string, string>>();

                foreach(var link in _parse_header_links(header))
                {
                    result.Add(link.GetValueOrDefault("rel") ?? link.GetValueOrDefault("url"), link);
                }

                return result;
             }
        }

        private List<Dictionary<string, string>> _parse_header_links(string value)
        {
            var links = new List<Dictionary<string, string>>();
            var replace_chars = new char[] { ' ', '"' };

            value = value.Trim(replace_chars);

            if(string.IsNullOrEmpty(value))
            {
                return links;
            }

            foreach (var val in value.Split(',', ' ', '*', '<'))
            {
                var items = val.Split(';', 1);

                var url = items[0];
                var para = items[1];

                var link = new Dictionary<string, string> { { "url", url.Trim("<> '\"".ToCharArray()) } };

                foreach(var p in para.Split(';'))
                {
                    var subItems = p.Split('=');

                    var key = subItems[0];
                    value = subItems[1];

                    link[key.Trim(replace_chars)] = value.Trim(replace_chars);
                }

                links.Add(link);
            }

            return links;
        }
    }
}
