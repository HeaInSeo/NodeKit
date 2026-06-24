namespace NodeKit.Authoring.Recipes
{
    /// <summary>
    /// User's answer to a ChangeMethodPreview's "계속할까요? [y/N]" prompt —
    /// see design doc Section 15.2.
    /// </summary>
    internal enum ChangeMethodDecision
    {
        Cancel,
        Proceed,
    }
}
