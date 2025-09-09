using Microsoft.Extensions.Logging;
using XIVSync.MareConfiguration;
using XIVSync.Services.Mediator;

namespace XIVSync.Interop;

public class LocalSoundController : DisposableMediatorSubscriberBase
{
	private readonly MareConfigService _mareConfigService;

	private bool _isLocallyMuted;

	public LocalSoundController(ILogger<LocalSoundController> logger, MareConfigService mareConfigService, MareMediator mareMediator)
		: base(logger, mareMediator)
	{
		_mareConfigService = mareConfigService;
		_isLocallyMuted = _mareConfigService.Current.MuteOwnSoundsLocally;
		base.Logger.LogInformation("[Local Sound] Sound controller initialized - Local muting is UI-only due to technical limitations");
		base.Logger.LogInformation("[Local Sound] To truly mute mod sounds locally, manually adjust FFXIV volume in Windows Volume Mixer when this option is enabled");
		base.Mediator.Subscribe<LocalSelfMuteSettingChangedMessage>(this, delegate
		{
			bool muteOwnSoundsLocally = _mareConfigService.Current.MuteOwnSoundsLocally;
			if (muteOwnSoundsLocally != _isLocallyMuted)
			{
				_isLocallyMuted = muteOwnSoundsLocally;
				base.Logger.LogInformation("[Local Sound] Local mute setting changed to: {muted}", _isLocallyMuted);
				if (_isLocallyMuted)
				{
					base.Logger.LogInformation("[Local Sound] Local mute enabled - Please manually lower FFXIV volume in Windows Volume Mixer for full effect");
				}
				else
				{
					base.Logger.LogInformation("[Local Sound] Local mute disabled - You can restore FFXIV volume in Windows Volume Mixer");
				}
			}
		});
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			base.Logger.LogTrace("[Local Sound] Sound controller disposed");
		}
		base.Dispose(disposing);
	}
}
