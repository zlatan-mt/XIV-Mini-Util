// Path: projects/XIV-Mini-Util/Services/CharaSelect/CharaSelectVoiceIdResolver.cs
// Description: キャラ作成用の声番号から再生用voice idを解決する
// Reason: ClientSelectData.VoiceIdを音声リソースIDとして直接扱わないため
namespace XivMiniUtil.Services.CharaSelect;

internal static class CharaSelectVoiceIdResolver
{
    public static ushort Resolve(byte selectedVoiceId, int voiceCount, Func<int, byte> getVoice)
    {
        if (voiceCount <= 0)
        {
            return selectedVoiceId;
        }

        if (selectedVoiceId < voiceCount)
        {
            var zeroBasedVoiceId = getVoice(selectedVoiceId);
            if (zeroBasedVoiceId != 0)
            {
                return zeroBasedVoiceId;
            }
        }

        if (selectedVoiceId > 0)
        {
            var oneBasedIndex = selectedVoiceId - 1;
            if (oneBasedIndex < voiceCount)
            {
                var oneBasedVoiceId = getVoice(oneBasedIndex);
                if (oneBasedVoiceId != 0)
                {
                    return oneBasedVoiceId;
                }
            }
        }

        return selectedVoiceId;
    }
}
