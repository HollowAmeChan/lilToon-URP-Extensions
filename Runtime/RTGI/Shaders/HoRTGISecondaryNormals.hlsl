#ifndef HO_RTGI_SECONDARY_NORMALS_INCLUDED
#define HO_RTGI_SECONDARY_NORMALS_INCLUDED

// Reproduce each raster vertex producer before barycentric interpolation.
// Normalizing after interpolating object-space attributes changes the surface
// under nonuniform scale, even before a normal map is sampled.
struct HoGISpecializedFrame
{
    float3 normal;
    float3 tangent;
    float3 bitangent;
    float3 fallback;
    float facing;
    bool mapped;
};

float3 HoGIVertexNormalWS(uint vertex)
{
    float3 normal=UnityRayTracingFetchVertexAttribute3(vertex,kVertexAttributeNormal);
    return HoGISafeNormal(mul(normal,(float3x3)WorldToObject3x4()),0);
}

float4 HoGIVertexTangentWS(uint vertex,bool normalizeVertex)
{
    float4 tangent=UnityRayTracingFetchVertexAttribute4(vertex,kVertexAttributeTangent);
    tangent.xyz=mul((float3x3)ObjectToWorld3x4(),tangent.xyz);
    if(normalizeVertex)tangent.xyz=HoGISafeNormal(tangent.xyz,0);
    tangent.w*=determinant((float3x3)ObjectToWorld3x4())<0?-1:1;
    return tangent;
}

HoGISpecializedFrame HoGIGetSpecializedFrame(uint3 tri,float3 weights,float face)
{
    HoGISpecializedFrame frame=(HoGISpecializedFrame)0;
    float3 interpolatedNormal=HoGIVertexNormalWS(tri.x)*weights.x+HoGIVertexNormalWS(tri.y)*weights.y+HoGIVertexNormalWS(tri.z)*weights.z;
    bool normalizeVertexTangent=_HoRTMaterialFamily==2||_HoRTMaterialFamily==5||_HoRTMaterialFamily==6;
    float4 interpolatedTangent=HoGIVertexTangentWS(tri.x,normalizeVertexTangent)*weights.x
        +HoGIVertexTangentWS(tri.y,normalizeVertexTangent)*weights.y+HoGIVertexTangentWS(tri.z,normalizeVertexTangent)*weights.z;
    float3 meshNormal=HoGISafeNormal(interpolatedNormal,float3(0,1,0));
    frame.fallback=meshNormal;
    frame.facing=_FlipNormal>.5?face:1;

    if(_HoRTMaterialFamily==2)
    {
        // Environment uses the interpolated, non-orthogonalized vertex frame;
        // it normalizes only the mapped result in GetEnvironmentNormalWS.
        frame.normal=interpolatedNormal;
        frame.tangent=interpolatedTangent.xyz;
        frame.bitangent=interpolatedTangent.w*cross(interpolatedNormal,interpolatedTangent.xyz);
        frame.mapped=true;
        return frame;
    }

    bool hair=_HoRTMaterialFamily==4;
    frame.normal=hair?meshNormal*face:meshNormal;
    frame.fallback=frame.normal;
    if(hair)frame.facing=1;
    float3 tangent=interpolatedTangent.xyz;
    if(_HoRTMaterialFamily==3)
    {
        // ClothBuildSilkFrame validates normalized shader-visible source axes.
        float3 tangentDirection=HoGISafeNormal(tangent,0);
        float3 bitangentDirection=HoGISafeNormal(cross(meshNormal,tangent)*interpolatedTangent.w,0);
        float handedness=dot(cross(meshNormal,tangentDirection),bitangentDirection);
        tangent=tangentDirection-meshNormal*dot(meshNormal,tangentDirection);
        float lengthSquared=dot(tangent,tangent);
        if(abs(handedness)<=1e-6||lengthSquared<=1e-12)return frame;
        frame.tangent=tangent*rsqrt(lengthSquared);
        frame.bitangent=cross(meshNormal,frame.tangent)*(handedness<0?-1:1);
    }
    else
    {
        tangent-=frame.normal*dot(frame.normal,tangent);
        float lengthSquared=dot(tangent,tangent);
        if(lengthSquared<(hair?1e-8:1e-6)||(hair&&abs(interpolatedTangent.w)<.5))return frame;
        frame.tangent=tangent*rsqrt(lengthSquared);
        // Hair takes sign(w); Skin/Eye preserve the interpolated w multiplier.
        frame.bitangent=cross(frame.normal,frame.tangent)*(hair?sign(interpolatedTangent.w):interpolatedTangent.w);
    }
    frame.mapped=true;
    return frame;
}
#endif
