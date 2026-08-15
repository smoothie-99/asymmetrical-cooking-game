public class SilverMimicBoxItem : MimicBoxVariantBaseItem
{
    protected override MimicBoxType BoxType => MimicBoxType.Silver;
    protected override MimicBoxSolveAction RequiredSolveAction => MimicBoxSolveAction.Wash;
}
