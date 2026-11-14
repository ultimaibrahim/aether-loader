namespace Aether.API.UI
{
    /// <summary>
    /// SPI contract for UI rendering provided by Aether.UI.
    /// Decouples the Aether.Loader bootstrap loop from direct IMGUI dependencies.
    /// </summary>
    public interface IUIService
    {
        void RenderGUI();
    }
}
