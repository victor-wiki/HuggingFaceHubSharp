using Newtonsoft.Json;

namespace HuggingFaceHubSharp.utils
{
    public class pagination
    {
        /// <summary>
        /// Fetch a list of models/datasets/spaces and paginate through results.
        /// 
        /// This is using the same "Link" header format as GitHub.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="parameters"></param>
        /// <param name="headers"></param>
        /// <param name="timeout"></param>
        /// <returns></returns>
        public static async Task<List<Dictionary<string, dynamic>>> paginate(string path, Dictionary<string, string> parameters, Dictionary<string, string> headers, float? timeout = null)
        {
            List<Dictionary<string, dynamic>> results = new List<Dictionary<string, dynamic>>();

            var r = await http.getResponse(path, parameters: parameters, headers: headers, timeout: timeout);

            Action<HttpResponseMessage> addContent = async (response) =>
            {
                var content = await http.getContent(response);

                if (content.Trim().StartsWith("["))
                {
                    results.AddRange(JsonConvert.DeserializeObject<List<Dictionary<string, dynamic>>>(content));
                }
                else
                {
                    results.Add(JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(content));
                }
            };

            await http.hf_raise_for_status(r);

            addContent(r);

            // Follow pages
            // Next link already contains query params
            var next_page = _get_next_page(r);

            while (next_page is not null)
            {
                //Console.WriteLine($"Pagination detected. Requesting next page: {next_page}");
                r = await http.getResponse(next_page, headers: headers, timeout: timeout);

                addContent(r);

                next_page = _get_next_page(r);
            }

            return results;
        }

        private static string _get_next_page(HttpResponseMessage response)
        {
            var links = http.getLinks(response);

            return (links.GetValueOrDefault("next") ?? new Dictionary<string, string>()).GetValueOrDefault("url");
        }
    }
}
