public class GoldMimicBoxItem : MimicBoxVariantBaseItem
{
    protected override MimicBoxType BoxType => MimicBoxType.Gold;
    protected override MimicBoxSolveAction RequiredSolveAction => MimicBoxSolveAction.Heat;
}
