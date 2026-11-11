cbuffer Scene : register(b0) { float frame; float scene; float2 padding; };
static const float Pi=3.14159265;
static const float3 Key=float3(-0.50,0.79,0.36);

float4 vsMain(uint vertex : SV_VertexID) : SV_Position {
  float2 corner=float2((vertex<<1)&2,vertex&2);
  return float4(corner*float2(2,-2)+float2(-1,1),0,1);
}
float solidBox(float3 p,float3 dimensions,float rounded){
  float3 d=abs(p)-dimensions+rounded;
  return length(max(d,0.0))+min(max(d.x,max(d.y,d.z)),0.0)-rounded;
}
float2 nearest(float2 best,float value,float material){
  return value<best.x ? float2(value,material) : best;
}
float3 stripe(float3 p,float spacing){
  p.x=(frac(p.x/spacing+0.5)-0.5)*spacing;
  return p;
}

float2 processor(float3 p,float2 objectHit){
  objectHit=nearest(objectHit,solidBox(p-float3(0,0.25,0),float3(0.94,0.08,0.85),0.054),2);
  objectHit=nearest(objectHit,solidBox(p-float3(0,0.39,0),float3(0.76,0.11,0.68),0.085),3);
  objectHit=nearest(objectHit,solidBox(p-float3(0,0.505,0),float3(0.48,0.010,0.37),0.018),4);
  float3 contacts=p-float3(0,0.205,0);
  float3 edges=float3(contacts.x,contacts.y,abs(contacts.z)-0.88);
  objectHit=nearest(objectHit,max(solidBox(stripe(edges,0.115),float3(0.022,0.04,0.08),0.01),abs(contacts.x)-0.90),5);
  edges=float3(contacts.z,contacts.y,abs(contacts.x)-0.97);
  objectHit=nearest(objectHit,max(solidBox(stripe(edges,0.115),float3(0.022,0.04,0.08),0.01),abs(contacts.z)-0.80),5);
  return objectHit;
}

float2 memoryBoard(float3 p,float2 objectHit){
  objectHit=nearest(objectHit,solidBox(p-float3(0,0.26,0),float3(1.42,0.075,0.44),0.035),2);
  float3 chips=p-float3(0,0.40,0);
  objectHit=nearest(objectHit,max(solidBox(stripe(chips,0.39),float3(0.145,0.10,0.28),0.033),abs(p.x)-1.31),4);
  float3 pins=p-float3(0,0.246,0.464);
  objectHit=nearest(objectHit,max(solidBox(stripe(pins,0.064),float3(0.014,0.028,0.058),0.008),abs(p.x)-1.32),5);
  return objectHit;
}

float2 solidState(float3 p,float2 objectHit){
  objectHit=nearest(objectHit,solidBox(p-float3(0,0.20,0),float3(1.44,0.07,0.50),0.06),2);
  objectHit=nearest(objectHit,solidBox(p-float3(-0.81,0.35,0),float3(0.28,0.10,0.36),0.028),3);
  objectHit=nearest(objectHit,solidBox(p-float3(-0.10,0.35,0),float3(0.32,0.10,0.36),0.028),4);
  objectHit=nearest(objectHit,solidBox(p-float3(0.69,0.35,0),float3(0.35,0.10,0.36),0.028),4);
  float3 pins=p-float3(1.43,0.24,0);
  pins=pins.zyx;
  objectHit=nearest(objectHit,max(solidBox(stripe(pins,0.066),float3(0.018,0.025,0.11),0.008),abs(p.z)-0.46),5);
  return objectHit;
}

float2 world(float3 p){
  float2 objectHit=float2(p.y+0.25,1);
  if(scene<0.5) return processor(p,objectHit);
  if(scene<1.5) return memoryBoard(p,objectHit);
  if(scene<2.5) return solidState(p,objectHit);
  objectHit=nearest(objectHit,solidBox(p-float3(0,0.11,0),float3(1.53,0.09,0.97),0.045),2);
  objectHit=processor((p-float3(-0.55,0.10,-0.09))*1.8,objectHit*float2(1.8,1));
  objectHit.x/=1.8;
  float3 mem=(p-float3(0.60,0.09,-0.15))*2.25;
  objectHit=memoryBoard(mem.zyx,objectHit*float2(2.25,1));
  objectHit.x/=2.25;
  objectHit=solidState((p-float3(0.25,0.09,0.70))*2.8,objectHit*float2(2.8,1));
  objectHit.x/=2.8;
  return objectHit;
}

float3 surfaceNormal(float3 p){
  float2 e=float2(0.0015,0);
  return normalize(float3(world(p+e.xyy).x-world(p-e.xyy).x,
    world(p+e.yxy).x-world(p-e.yxy).x,world(p+e.yyx).x-world(p-e.yyx).x));
}
float softShadow(float3 origin){
  float t=0.035, result=1.0;
  [loop] for(int i=0;i<16;i++){
    float gap=world(origin+Key*t).x;
    result=min(result,10.0*gap/t);
    t+=clamp(gap,0.035,0.26);
  }
  return clamp(result,0.2,1.0);
}
float3 materialColour(float id,float3 p){
  if(id<1.5){
    float2 grid=abs(frac(p.xz*2.0+0.5)-0.5);
    float outline=1.0-smoothstep(0.007,0.013,min(grid.x,grid.y));
    return float3(0.055,0.092,0.154)+outline*float3(0.025,0.041,0.065);
  }
  if(id<2.5){
    float trace=1.0-smoothstep(0.015,0.024,abs(frac((p.z+abs(p.x)*0.11)*9.0)-0.5));
    return float3(0.048,0.22,0.27)+trace*float3(0.055,0.16,0.14);
  }
  if(id<3.5)return float3(0.36,0.48,0.64);
  if(id<4.5){
    float label=step(0.0,sin((p.x+p.z*0.35)*140.0))*0.045;
    return float3(0.085,0.13,0.21)+label;
  }
  return float3(0.63,0.37,0.12);
}

float3 traffic(float3 origin,float3 direction,float depth){
  float3 glow=0;
  float orientation=scene<0.5 ? 1.0 : -1.0;
  [loop] for(int stream=0;stream<4;stream++){
    float track=(float(stream)-1.5)*0.41;
    [unroll] for(int pulse=0;pulse<3;pulse++){
      float t=frac(frame+float(pulse)/3.0+float(stream)*0.12);
      float signDirection=stream%2==0 ? orientation : -orientation;
      float position=signDirection*(t*5.2-2.6);
      float3 packet=float3(position,0.10+0.42*exp(-position*position*1.15),track);
      if(scene>=0.5 && scene<1.5) packet.xz=packet.zx;
      float along=dot(packet-origin,direction);
      if(along<=0 || along>depth+0.075) continue;
      float3 offset=origin+direction*along-packet;
      float across=length(offset);
      float shape=exp(-across*across*115.0);
      float core=exp(-across*across*690.0);
      float3 tint=stream%2==0 ? float3(0.11,0.79,1.0) : float3(0.29,1.0,0.67);
      glow+=(shape*0.35+core*1.22)*tint;
    }
  }
  return glow;
}

float4 psMain(float4 screen : SV_Position) : SV_Target {
  float2 uv=(screen.xy-float2(320,180))/360.0;
  uv.y=-uv.y;
  float angle=0.54+0.24*sin(frame*2.0*Pi);
  float3 eye=float3(4.0*sin(angle),2.58,4.0*cos(angle));
  float3 look=normalize(float3(0,0.23,0)-eye);
  float3 right=normalize(cross(look,float3(0,1,0)));
  float3 up=cross(right,look);
  float3 ray=normalize(look+uv.x*right+uv.y*up);
  float distance=0.0;
  float2 hit=float2(1,0);
  [loop] for(int i=0;i<68;i++){
    hit=world(eye+distance*ray);
    if(hit.x<0.0018 || distance>15.0) break;
    distance+=hit.x;
  }
  float3 haze=lerp(float3(0.028,0.057,0.12),float3(0.10,0.23,0.38),saturate(0.65+uv.y));
  float3 colour=haze;
  if(hit.x<0.0018 && distance<15.0){
    float3 p=eye+distance*ray;
    float3 n=surfaceNormal(p);
    float light=saturate(dot(n,Key))*softShadow(p+n*0.013);
    float gloss=pow(saturate(dot(reflect(-Key,n),-ray)),32.0);
    float rim=pow(1.0-saturate(dot(n,-ray)),4.0);
    colour=materialColour(hit.y,p)*(0.44+0.85*light)+float3(0.26,0.48,0.75)*(0.40*rim+0.70*gloss);
    colour=lerp(colour,haze,smoothstep(6.0,14.0,distance));
  }
  colour+=traffic(eye,ray,distance);
  colour*=1.05-0.30*dot(uv,uv);
  return float4(pow(saturate(colour),float3(0.78,0.78,0.78)),1.0);
}
