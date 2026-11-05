using StandUp.Core;

namespace StandUp.Tests;

public class HealthCopyTests
{
    // SPEC 3.0:App 內任何文字都不得使用這些字眼。
    private static readonly string[] Forbidden =
    {
        "醫學證實", "醫學證明", "保證", "治療", "治癒", "根治", "預防疾病", "證實",
    };

    [Fact]
    public void NoTextContainsForbiddenWording()
    {
        foreach (var text in HealthCopy.AllTexts)
        {
            foreach (var word in Forbidden)
                Assert.DoesNotContain(word, text);
        }
    }

    [Fact]
    public void ScanCoversASubstantialSetOfTexts()
    {
        // 防止 AllTexts 被改空而讓上面的掃描形同虛設。
        Assert.True(HealthCopy.AllTexts.Count() >= 15);
        Assert.All(HealthCopy.AllTexts, text => Assert.False(string.IsNullOrWhiteSpace(text)));
    }

    [Fact]
    public void AllThreeStudiesAreCited()
    {
        Assert.Equal(3, HealthCopy.Studies.Count);
        Assert.Contains(HealthCopy.Studies, s => s.Citation.Contains("Medicine & Science in Sports & Exercise") && s.Citation.Contains("2023"));
        Assert.Contains(HealthCopy.Studies, s => s.Citation.Contains("Diabetes Care") && s.Citation.Contains("2012"));
        Assert.Contains(HealthCopy.Studies, s => s.Citation.Contains("Annals of Internal Medicine") && s.Citation.Contains("2017"));
    }

    [Fact]
    public void RequiredWordingIsPresent()
    {
        Assert.Equal("本工具不是醫療建議,若有健康疑慮請諮詢醫師。", HealthCopy.Disclaimer);
        Assert.StartsWith("研究顯示", HealthCopy.StandardRecommendation);
        Assert.Contains("無法驗證", HealthCopy.CannotVerifyWalking);
        Assert.Contains("小樣本", HealthCopy.StudyLimitations);
        Assert.Contains("短期", HealthCopy.StudyLimitations);
        Assert.Contains("不是醫療建議", HealthCopy.OverlayDisclaimer);
    }

    [Fact]
    public void StudySummariesUseResearchShowsWording()
    {
        Assert.All(HealthCopy.Studies, s => Assert.Contains("研究顯示", s.Summary));
    }
}
