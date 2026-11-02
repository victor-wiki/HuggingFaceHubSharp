namespace HuggingFaceHubSharp.Model
{
    public class SafeTensorsInfo : Dictionary<string, dynamic>
    {
        public Dictionary<string, int> parameters { get; set; }
        public int total { get; set; }
    }
}
