using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WikiAI
{
    public class IndexModel(WikiCopilot _wc) : PageModel
    {
        public IReadOnlyDictionary<string, string> Strategies { get; set; } = new Dictionary<string, string>();
        public void OnGet()
        {
            Strategies = _wc.ListStrategies();
        }
    }
}
