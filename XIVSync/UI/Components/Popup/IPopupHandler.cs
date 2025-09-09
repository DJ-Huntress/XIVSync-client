using System.Numerics;

namespace XIVSync.UI.Components.Popup;

public interface IPopupHandler
{
	Vector2 PopupSize { get; }

	bool ShowClose { get; }

	void DrawContent();
}
