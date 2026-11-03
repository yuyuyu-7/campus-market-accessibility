"""每功能20次、5并发的短时抽样，不替代10分钟压测或7天可用性验证。"""
import urllib.request,urllib.error,json,uuid,time,base64,struct,zlib,concurrent.futures,pathlib,sys
BASE=sys.argv[1] if len(sys.argv)>1 else 'http://127.0.0.1:5079'
OUT=pathlib.Path(sys.argv[2] if len(sys.argv)>2 else 'performance-smoke.json')
def call(m,p,b=None,t='',expected=200):
 h={'Content-Type':'application/json','X-Client-Id':'perf-smoke','Idempotency-Key':uuid.uuid4().hex}
 if t:h['Authorization']='Bearer '+t
 req=urllib.request.Request(BASE+p,method=m,headers=h,data=None if m=='GET' else json.dumps(b).encode())
 start=time.perf_counter()
 with urllib.request.urlopen(req,timeout=20) as r:
  d=json.load(r);assert r.status==expected,(r.status,d)
 return d['data'],round((time.perf_counter()-start)*1000,3)
def reg(a):return call('POST','/api/v1/users',{'account':a,'password':'Course123!','nickname':a,'contact':'perf@example.test'},expected=201)
prefix='pf_'+uuid.uuid4().hex[:8]
reg(prefix+'_seller');reg(prefix+'_buyer')
s=call('POST','/api/v1/sessions',{'account':prefix+'_seller','password':'Course123!'})[0]['token']
b=call('POST','/api/v1/sessions',{'account':prefix+'_buyer','password':'Course123!'})[0]['token']
def chunk(t,d):return struct.pack('!I',len(d))+t+d+struct.pack('!I',zlib.crc32(t+d)&0xffffffff)
png=b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('!2I5B',1,1,8,2,0,0,0))+chunk(b'IDAT',zlib.compress(b'\0\x80\x80\x80'))+chunk(b'IEND',b'')
img=call('POST','/api/v1/images',{'fileName':'perf.png','mimeType':'image/png','contentBase64':base64.b64encode(png).decode()},s,201)[0]['imageId']
results=[]
def measure(name,fn):
 def attempt(i):
  start=time.perf_counter()
  try:d,ms=fn(i);return {'ok':True,'ms':ms,'data':d}
  except Exception as e:return {'ok':False,'ms':round((time.perf_counter()-start)*1000,3),'error':str(e)}
 with concurrent.futures.ThreadPoolExecutor(5) as pool:raw=list(pool.map(attempt,range(20)))
 times=sorted(x['ms'] for x in raw);r={'function':name,'samples':20,'concurrency':5,'p95Ms':times[18],'technicalSuccessRate':sum(x['ok'] for x in raw)/20,'errors':[x.get('error') for x in raw if not x['ok']]};results.append(r);print(r,flush=True);assert not r['errors'],r
 return [x['data'] for x in raw]
measure('U01 注册',lambda i:reg(prefix+'_'+str(i)))
measure('U02 登录',lambda i:call('POST','/api/v1/sessions',{'account':prefix+'_seller','password':'Course123!'}))
measure('U03 资料读取',lambda i:call('GET','/api/v1/me',t=s))
products=measure('P01 商品发布',lambda i:call('POST','/api/v1/products',{'title':'性能样品'+str(i),'category':'教材','description':'虚构测试样品','priceCents':100,'imageIds':[img]},s,201))
time.sleep(1)
products=[call('GET','/api/v1/products/'+p['id'],t=s)[0] for p in products]
assert all(p['status']=='在售' for p in products)
measure('P02 商品浏览',lambda i:call('GET','/api/v1/products'))
measure('P03 本人商品读取',lambda i:call('GET','/api/v1/me/products',t=s))
trades=measure('T01 申请交易',lambda i:call('POST','/api/v1/trades',{'productId':products[i]['id'],'productVersion':products[i]['version']},b,201))
accepted=measure('T02 接受交易',lambda i:call('POST','/api/v1/trades/'+trades[i]['id']+'/accept',{'version':trades[i]['version'],'productVersion':products[i]['version']},s))
measure('T03 交易查询',lambda i:call('GET','/api/v1/trades',t=s))
measure('T04 完成交易',lambda i:call('POST','/api/v1/trades/'+trades[i]['id']+'/complete',{'version':accepted[i]['version'],'productVersion':accepted[i]['productVersion']},b))
probes=[]
for i in range(10):
 d,ms=call('GET','/ready');probes.append({'ok':d.get('ready') is True,'ms':ms});time.sleep(.2)
OUT.parent.mkdir(parents=True,exist_ok=True);OUT.write_text(json.dumps({'timestamp':time.strftime('%Y-%m-%d %H:%M:%S'),'kind':'短时抽样，不是长时间压测或7天可用性验证','results':results,'readinessProbeSamples':probes},ensure_ascii=False,indent=2),encoding='utf-8')
