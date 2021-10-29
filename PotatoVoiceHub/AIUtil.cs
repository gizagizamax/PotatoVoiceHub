using System.Reflection;
using AI.Framework;
using AI.Framework.Wpf;
using AI.Talk.Editor;

namespace PotatoVoiceHub
{
    public class AIUtil
    {
        public static MainPresenter GetMainPresenter()
        {
            return MainPresenter.Current;
        }

        public static PresenterBase<TextEditView, TextEditViewModel> GetTextEditPresenter()
        {
            return (PresenterBase<TextEditView, TextEditViewModel>) typeof(MainPresenter).GetProperty("TextEditPresenter", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(MainPresenter.Current);
        }

        public static MainModel GetMainModel()
        {
            return MainModel.Current;
        }
    }
}
