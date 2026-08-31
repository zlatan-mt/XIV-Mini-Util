namespace XivMiniUtil.Services.TitleBackground;

internal static class TitleBackgroundEnvironmentWeatherPolicy
{
    public static byte ResolveRequestedWeatherId(string? candidateId)
    {
        return string.Equals(
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.NormalizeId(candidateId),
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.ElpisCandidateId,
            StringComparison.Ordinal)
            ? TitleBackgroundEnvironmentClearSkyWriter.FairSkiesWeatherId
            : TitleBackgroundEnvironmentClearSkyWriter.ClearSkiesWeatherId;
    }
}
