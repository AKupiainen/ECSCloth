Shader "Custom/DoubleSided_BaseColorOverlay"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.5,0.5,0.5,1)
        _OverlayMap ("Overlay Texture", 2D) = "white" {}
        _OverlayColor ("Overlay Color Tint", Color) = (1,1,1,1)
        _OverlayStrength ("Overlay Blend Strength", Range(0,1)) = 1.0
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        // Disable culling to render both front and back faces
        Cull Off

        CGPROGRAM
        // Physically based Standard lighting model, and enable shadows on all light types
        #pragma surface surf Standard fullforwardshadows

        // Use shader model 3.0 target to get nicer looking lighting
        #pragma target 3.0

        sampler2D _OverlayMap;

        struct Input
        {
            float2 uv_OverlayMap;
            float facing : VFACE;
        };

        half _Glossiness;
        half _Metallic;
        fixed4 _BaseColor;
        fixed4 _OverlayColor;
        float _OverlayStrength;

        // Add instancing support for this shader. You need to check 'Enable Instancing' on materials that use the shader.
        #pragma instancing_options assumeuniformscaling
        UNITY_INSTANCING_BUFFER_START(Props)
            // put more per-instance properties here
        UNITY_INSTANCING_BUFFER_END(Props)

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            // Get the overlay texture
            fixed4 overlayTexture = tex2D(_OverlayMap, IN.uv_OverlayMap) * _OverlayColor;
            
            // Blend the overlay texture with the base color
            // Use the overlay texture's alpha and the strength parameter to control the blend
            fixed4 finalColor = lerp(_BaseColor, overlayTexture, overlayTexture.a * _OverlayStrength);
            
            // Apply the final color to the albedo
            o.Albedo = finalColor.rgb;
            
            // Metallic and smoothness come from slider variables
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = finalColor.a;
            
            // Handle double-sided normals
            // If we're viewing the back face, flip the normal
            if (IN.facing < 0) {
                o.Normal = float3(0, 0, -1);
            }
        }
        ENDCG
    }
    FallBack "Diffuse"
}