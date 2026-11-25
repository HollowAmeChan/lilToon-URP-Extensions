using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.GTAO
{
    internal static class HoAOGlobalState
    {
        internal static void Bind(ScriptableRenderContext context, Texture texture, float available, bool enqueue = true)
        {
            Shader.SetGlobalTexture(HoGTAOShaderConstants.AOTextureId, texture);
            Shader.SetGlobalFloat(HoGTAOShaderConstants.AOAvailableId, available);
            if (!enqueue) return;

            CommandBuffer command = CommandBufferPool.Get("Ho-AO Camera State");
            try
            {
                command.SetGlobalTexture(HoGTAOShaderConstants.AOTextureId, texture);
                command.SetGlobalFloat(HoGTAOShaderConstants.AOAvailableId, available);
                context.ExecuteCommandBuffer(command);
            }
            finally
            {
                CommandBufferPool.Release(command);
            }
        }
    }
}
