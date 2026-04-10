Shader "Custom/OutlineShaderVR"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (1, 1, 0, 1)
        _OutlineWidth ("Outline Width", Range(0, 0.1)) = 0.03
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Transparent" }

        Pass
        {
            Name "OUTLINE"
            Cull Front
            ZWrite On
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // Required for VR Single Pass Instancing
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"
c
            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                // Add this for VR instance ID
                UNITY_VERTEX_INPUT_INSTANCE_ID 
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                // Add this to pass instance ID to fragment (if needed)
                UNITY_VERTEX_OUTPUT_STEREO 
            };

            float _OutlineWidth;
            fixed4 _OutlineColor;

            v2f vert(appdata v)
            {
                v2f o;
                // Initialize VR macros
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                // 1. Move vertex to Clip Space
                o.pos = UnityObjectToClipPos(v.vertex);

                // 2. Fix the VR Normal Projection
                // Instead of IT_MV, we use UnityObjectToViewPos to get the view-space normal
                // which is more reliable across different VR rendering modes.
                float3 viewNormal = mul((float3x3)UNITY_MATRIX_IT_MV, v.normal);
                
                // 3. Project to 2D screen space
                float2 offset = TransformViewToProjection(viewNormal.xy);

                // 4. Apply offset with perspective correction
                o.pos.xy += offset * o.pos.w * _OutlineWidth;

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Ensure the fragment shader knows which eye it's rendering
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                return _OutlineColor;
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}