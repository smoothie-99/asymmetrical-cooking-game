public class BronzeMimicBoxItem : MimicBoxVariantBaseItem
{
    protected override MimicBoxType BoxType => MimicBoxType.Bronze;
    protected override MimicBoxSolveAction RequiredSolveAction => MimicBoxSolveAction.Knife;
}
