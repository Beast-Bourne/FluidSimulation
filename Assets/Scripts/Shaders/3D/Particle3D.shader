Shader "Hidden/Particle3D"
{
    Properties
    {
        _Colour("Colour", Color) = (0,0,1,1)
        _Radius ("Radius", Float) = 0.5
        _MainTex("Base (RGB)", 2D) = "White" {}
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
		Blend SrcAlpha OneMinusSrcAlpha
		ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            StructuredBuffer<float2> positions;
            StructuredBuffer<float2> velocities;
            StructuredBuffer<float> densities;
            float scale;
            Texture2D<float4> ColourMap;
            SamplerState linear_clamp_sampler;
            float densityMax;
            float velocityMax;
            bool useVelocity;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos: POSITION;
                float2 uv : TEXCOORD0;
                float3 colour : TEXCOORD1;
            };

            float _Radius;
            float4 _Colour;
            float4 _Centre;

            v2f vert (appdata v, uint instanceID : SV_InstanceID)
            {
                float density = densities[instanceID];
                float densityT = saturate(density / densityMax);
                float colT = densityT;

                if (useVelocity)
                {
                    float velocity = velocities[instanceID];
                    float velocityT = saturate(velocity / velocityMax);
                    colT = velocityT;
                }

                float3 centreWorld = float3(positions[instanceID],0);
                float3 worldVertPos = centreWorld + mul(unity_ObjectToWorld, v.vertex * scale);
                float3 objectVertPos = mul(unity_WorldToObject, float4(worldVertPos.xyz, 1));

                v2f o;
                o.pos = UnityObjectToClipPos(objectVertPos);
                o.uv = v.uv;
                o.colour = ColourMap.SampleLevel(linear_clamp_sampler, float2(colT, 0.5), 0);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 centreOffset = (i.uv.xy - 0.5) *2;
                float sqrDist = dot(centreOffset, centreOffset);
                float delta = fwidth(sqrt(sqrDist));
                float alpha = 1 - smoothstep(1-delta, 1+ delta, sqrDist);

                float3 colour = i.colour;
                return float4(colour, alpha);
            }
            ENDCG
        }
    }
}
