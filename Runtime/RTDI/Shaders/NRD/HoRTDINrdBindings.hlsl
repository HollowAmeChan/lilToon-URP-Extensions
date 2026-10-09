#define NRD_CONSTANTS_START(name) cbuffer HoRTDINrdConstants {
#define NRD_CONSTANT(type,name) type name;
#define NRD_CONSTANTS_END };
#define NRD_INPUTS_START
#define NRD_INPUT(type,data,name,reg,index) type<data> name;
#define NRD_INPUTS_END
#define NRD_OUTPUTS_START
#define NRD_OUTPUT(type,data,name,reg,index) type<data> name;
#define NRD_OUTPUTS_END
#define NRD_SAMPLERS_START
#define NRD_SAMPLER(type,name,reg,index) type name;
#define NRD_SAMPLERS_END
#define gNearestClamp sampler_PointClamp
#define gLinearClamp sampler_LinearClamp
