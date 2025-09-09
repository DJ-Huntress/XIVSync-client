using XIVSync.API.Dto.Group;
using XIVSync.PlayerData.Pairs;

namespace XIVSync.Services.Mediator;

public record OpenBanUserPopupMessage(Pair PairToBan, GroupFullInfoDto GroupFullInfoDto) : MessageBase();
