namespace HuggingFaceHubSharp.utils
{
    public class runtime
    {
        public static bool is_xet_available()
        {
            // since hf_xet is automatically used if available, allow explicit disabling via environment variable
            if (constants.HF_HUB_DISABLE_XET)
            {
                return false;
            }

            return is_package_available("hf_xet");
        }

        public static bool is_package_available(string package_name)
        {
            return _get_version(package_name) != "N/A";
        }

        private static string _get_version(string package_name)
        {
            return "N/A"; ////to do
        }
    }
}
