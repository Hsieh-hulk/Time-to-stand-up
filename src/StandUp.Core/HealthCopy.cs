namespace StandUp.Core;

/// <summary>一份引用的研究:標題列、完整引用、一段白話摘要。</summary>
public sealed record StudyCitation(string Heading, string Citation, string Summary);

/// <summary>
/// App 內所有和健康相關的文字都集中在這裡(SPEC 3.0 文案規則)。
/// 用「研究顯示」,不用「醫學證實 / 醫學證明 / 保證 / 治療」這類字眼;
/// 單元測試會掃描 <see cref="AllTexts"/> 確認沒有禁用字眼。
/// 研究數字已對照 PubMed / PMC 摘要查證;查不到的數字(例如血糖降幅百分比)不放進來。
/// </summary>
public static class HealthCopy
{
    public const string ResearchHeading = "研究依據";

    public const string ResearchIntro =
        "預設的「工作 30 分鐘、起身走動 5 分鐘」不是隨意設定的,而是參考下列研究。"
        + "世界衛生組織 2020 年指引(Bull 等人,Br J Sports Med)也建議減少久坐、以任何強度的身體活動取代久坐時間,"
        + "但沒有訂出久坐時間的上限。";

    /// <summary>首次啟動畫面與設定頁共用的建議說明。</summary>
    public const string StandardRecommendation =
        "研究顯示,每 30 分鐘起身走動約 5 分鐘,有助於降低餐後血糖與血壓。";

    public const string Disclaimer = "本工具不是醫療建議,若有健康疑慮請諮詢醫師。";

    public const string LimitationsHeading = "這些研究的限制";

    public const string StudyLimitations =
        "前兩份是小樣本、短期、在受控環境下進行的實驗,觀察的是走動後短時間內的反應,"
        + "不能視為長期健康效果的依據;第三份是觀察性研究,只能看出關聯,不能據此推論因果。";

    public const string CannotVerifyWalking =
        "本工具無法驗證你是否真的站起來走動,只能提醒你。";

    public const string AboutHeading = "關於 StandUp 站起來";

    public const string AboutText =
        "StandUp 站起來是在 Windows 上常駐的休息提醒工具:連續工作到設定時間後,以全螢幕畫面提醒你起身走動。"
        + "完全離線,不蒐集、不上傳任何資料。";

    /// <summary>休息畫面主螢幕上的提示。</summary>
    public const string OverlayHint = "研究顯示,規律起身走動有助於降低餐後血糖與血壓。";

    /// <summary>休息畫面底部的簡短聲明。</summary>
    public const string OverlayDisclaimer =
        "研究顯示的是小規模、短期實驗的結果,本工具不是醫療建議,若有健康疑慮請諮詢醫師。";

    public static IReadOnlyList<StudyCitation> Studies { get; } = new[]
    {
        new StudyCitation(
            "Duran 等人,2023(最後作者為 Diaz)",
            "Duran AT, Friel CP, Serafini MA, Ensari I, Cheung YK, Diaz KM. "
            + "Breaking Up Prolonged Sitting to Improve Cardiometabolic Risk: Dose-Response Analysis of a "
            + "Randomized Crossover Trial. Medicine & Science in Sports & Exercise. 2023.",
            "11 位中年與年長成人在實驗室連續坐 8 小時,比較 5 種安排:每 30 或 60 分鐘走 1 或 5 分鐘,以及完全不走。"
            + "研究顯示,只有「每 30 分鐘走 5 分鐘」使血糖反應顯著降低;所有走動安排的收縮壓都比持續坐著低,"
            + "降幅最大的是每 60 分鐘走 1 分鐘(5.2 mmHg)與每 30 分鐘走 5 分鐘(4.3 mmHg)。"),

        new StudyCitation(
            "Dunstan 等人,2012",
            "Dunstan DW, et al. Breaking up prolonged sitting reduces postprandial glucose and insulin responses. "
            + "Diabetes Care. 2012.",
            "19 位 45–65 歲過重或肥胖的成人,在久坐期間每 20 分鐘走 2 分鐘(輕度或中度強度)。"
            + "研究顯示,餐後血糖與胰島素的反應都比持續坐著低。"),

        new StudyCitation(
            "Diaz 等人,2017",
            "Diaz KM, et al. Patterns of Sedentary Behavior and Mortality in U.S. Middle-Aged and Older Adults: "
            + "A National Cohort Study. Annals of Internal Medicine. 2017.",
            "7,985 位 45 歲以上成人配戴加速度計,追蹤中位數 4 年。"
            + "研究顯示,總久坐時間較長、單次連續久坐時間較長,各自都與較高的全因死亡風險有關。"),
    };

    /// <summary>所有會顯示給使用者的健康相關文字,供單元測試掃描禁用字眼。</summary>
    public static IEnumerable<string> AllTexts
    {
        get
        {
            yield return ResearchHeading;
            yield return ResearchIntro;
            yield return StandardRecommendation;
            yield return Disclaimer;
            yield return LimitationsHeading;
            yield return StudyLimitations;
            yield return CannotVerifyWalking;
            yield return AboutHeading;
            yield return AboutText;
            yield return OverlayHint;
            yield return OverlayDisclaimer;
            foreach (var study in Studies)
            {
                yield return study.Heading;
                yield return study.Citation;
                yield return study.Summary;
            }
        }
    }
}
