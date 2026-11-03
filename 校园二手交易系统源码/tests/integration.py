"""真实 HTTP 集成测试。使用独立数据库启动 --server 后运行；只创建虚构测试数据。"""
import urllib.request,urllib.error,json,uuid,time,base64,struct,zlib,sys,concurrent.futures,pathlib
BASE=sys.argv[1] if len(sys.argv)>1 else 'http://127.0.0.1:5079'
OUT=pathlib.Path(sys.argv[2] if len(sys.argv)>2 else 'integration-results.json')
results=[]
def call(method,path,body=None,token='',key=None,expected=200):
 headers={'Content-Type':'application/json','X-Client-Id':'integration-client','Idempotency-Key':key or uuid.uuid4().hex}
 if token: headers['Authorization']='Bearer '+token
 r=urllib.request.Request(BASE+path,data=None if method=='GET' else json.dumps(body or {},ensure_ascii=False).encode(),headers=headers,method=method)
 start=time.perf_counter()
 try:
  with urllib.request.urlopen(r,timeout=20) as resp:status=resp.status;d=json.load(resp)
 except urllib.error.HTTPError as e:status=e.code;d=json.load(e)
 ms=round((time.perf_counter()-start)*1000,2)
 assert status==expected,(method,path,status,expected,d)
 return d,ms
def test(name,f):
 try:f();results.append({'name':name,'passed':True});print('PASS',name,flush=True)
 except Exception as e:results.append({'name':name,'passed':False,'error':str(e)});print('FAIL',name,str(e),flush=True)
def check(b):assert b
def data(*a,**kw):return call(*a,**kw)[0]['data']
suffix=uuid.uuid4().hex[:8]
accounts={r:f'{r}_{suffix}' for r in ['seller','buyer','other']}
tokens={}
def register_all():
 for role,a in accounts.items():
  data('POST','/api/v1/users',{'account':a,'password':'Course123!','nickname':role,'contact':role+'@example.test'},expected=201)
  tokens[role]=data('POST','/api/v1/sessions',{'account':a,'password':'Course123!'})['token']
test('U01/U02 注册三个账号并登录',register_all)
s=tokens['seller'];b=tokens['buyer'];c=tokens['other']
def png():
 def chunk(t,d):return struct.pack('!I',len(d))+t+d+struct.pack('!I',zlib.crc32(t+d)&0xffffffff)
 return b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('!2I5B',2,2,8,2,0,0,0))+chunk(b'IDAT',zlib.compress(b'\x00'+b'\x80\x80\xff'*2+b'\x00'+b'\x80\x80\xff'*2))+chunk(b'IEND',b'')
imgbody={'fileName':'book.png','mimeType':'image/png','contentBase64':base64.b64encode(png()).decode()}
image=data('POST','/api/v1/images',imgbody,s,expected=201)['imageId']
productbody={'title':'软件工程教材','category':'教材','description':'少量笔记，校内交接','priceCents':1800,'imageIds':[image]}
def product(**extra):return data('POST','/api/v1/products',dict(productbody,**extra),s,expected=201)
def wait_review(p):
 for _ in range(40):
  p=data('GET','/api/v1/products/'+p['id'],token=s)
  if p['status']!='待审核':return p
  time.sleep(.1)
 raise AssertionError('审核超时')
test('E01 未登录提交被401拦截',lambda:call('POST','/api/v1/products',productbody,expected=401))
test('E02 必填字段空白被422拦截',lambda:call('POST','/api/v1/products',dict(productbody,title='  '),s,expected=422))
test('E03 图片扩展名/MIME不符被拦截',lambda:call('POST','/api/v1/images',dict(imgbody,fileName='book.gif'),s,expected=422))
test('E03 图片伪造内容被拦截',lambda:call('POST','/api/v1/images',dict(imgbody,contentBase64=base64.b64encode(b'not-image').decode()),s,expected=422))
test('E04 图片超过2MiB被拦截',lambda:call('POST','/api/v1/images',dict(imgbody,contentBase64=base64.b64encode(b'x'*2097153).decode()),s,expected=422))
for price in [0,-1,1000001,1.5,'1800',None]:test('E05 非法价格 '+str(price),lambda price=price:call('POST','/api/v1/products',dict(productbody,priceCents=price),s,expected=422))
def duplicate():
 key=uuid.uuid4().hex;data('POST','/api/v1/products',productbody,s,key,201)
 d,_=call('POST','/api/v1/products',productbody,s,key,409);check(d['code']=='DUPLICATE_SUBMIT')
 d,_=call('POST','/api/v1/products',dict(productbody,title='changed'),s,key,409);check(d['code']=='IDEMPOTENCY_CONFLICT')
test('E06 重复提交与异体同键冲突',duplicate)
test('未知字段/伪造卖家被拦截',lambda:call('POST','/api/v1/products',dict(productbody,sellerId='fake'),s,expected=422))
test('他人图片不可绑定',lambda:call('POST','/api/v1/products',productbody,b,expected=422))
p=wait_review(product());pid=p['id']
test('异步审核公开与游客浏览',lambda:check(data('GET','/api/v1/products/'+pid)['status']=='在售'))
test('公开详情不泄漏联系方式',lambda:check('contact' not in data('GET','/api/v1/products/'+pid)))
test('本人不能购买自己的商品',lambda:call('POST','/api/v1/trades',{'productId':pid,'productVersion':p['version']},s,expected=403))
test('不能修改他人商品',lambda:call('PATCH','/api/v1/products/'+pid,dict(productbody,version=p['version']),b,expected=403))
test('过期版本拒绝',lambda:call('PATCH','/api/v1/products/'+pid,dict(productbody,version=1),s,expected=409))
t=data('POST','/api/v1/trades',{'productId':pid,'productVersion':p['version']},b,expected=201)
t2=data('POST','/api/v1/trades',{'productId':pid,'productVersion':p['version']},c,expected=201)
test('换幂等键重复申请仍拦截',lambda:call('POST','/api/v1/trades',{'productId':pid,'productVersion':p['version']},b,expected=409))
test('待处理申请时禁止编辑',lambda:call('PATCH','/api/v1/products/'+pid,dict(productbody,version=p['version']),s,expected=409))
test('第三人不能读取交易详情',lambda:call('GET','/api/v1/trades/'+t['id'],token=c,expected=403))
test('买家不能接受自己的申请',lambda:call('POST','/api/v1/trades/'+t['id']+'/accept',{'version':1,'productVersion':p['version']},b,expected=403))
accepted=data('POST','/api/v1/trades/'+t['id']+'/accept',{'version':1,'productVersion':p['version']},s)
test('接受后其他申请自动拒绝',lambda:check(data('GET','/api/v1/trades/'+t2['id'],token=c)['status']=='已拒绝'))
test('接受后买家可见对方联系方式',lambda:check(data('GET','/api/v1/trades/'+t['id'],token=b)['counterpartyContact']=='seller@example.test'))
test('交易中禁止下架',lambda:call('POST','/api/v1/products/'+pid+'/withdraw',{'version':accepted['productVersion']},s,expected=409))
completed=data('POST','/api/v1/trades/'+t['id']+'/complete',{'version':accepted['version'],'productVersion':accepted['productVersion']},b)
test('完整交易完成与商品售出',lambda:check(completed['status']=='已完成' and data('GET','/api/v1/products/'+pid,token=s)['status']=='已售出'))
test('已售商品对游客隐藏',lambda:call('GET','/api/v1/products/'+pid,expected=404))
test('敏感词审核驳回且不可公开',lambda:check(wait_review(product(title='违禁品测试'))['status']=='审核驳回'))
def cancel_reject_withdraw():
 p=wait_review(product());t=data('POST','/api/v1/trades',{'productId':p['id'],'productVersion':p['version']},b,expected=201)
 check(data('POST','/api/v1/trades/'+t['id']+'/cancel',{'version':t['version']},b)['status']=='已取消')
 t=data('POST','/api/v1/trades',{'productId':p['id'],'productVersion':p['version']},b,expected=201)
 check(data('POST','/api/v1/trades/'+t['id']+'/reject',{'version':t['version'],'productVersion':p['version']},s)['status']=='已拒绝')
 t=data('POST','/api/v1/trades',{'productId':p['id'],'productVersion':p['version']},b,expected=201)
 data('POST','/api/v1/products/'+p['id']+'/withdraw',{'version':p['version']},s)
 check(data('GET','/api/v1/trades/'+t['id'],token=b)['status']=='已拒绝')
test('取消、拒绝与下架一致性',cancel_reject_withdraw)
def concurrent_accept():
 p=wait_review(product());ts=[data('POST','/api/v1/trades',{'productId':p['id'],'productVersion':p['version']},x,expected=201) for x in (b,c)]
 def run(t):
  try:return data('POST','/api/v1/trades/'+t['id']+'/accept',{'version':1,'productVersion':p['version']},s)['status']
  except AssertionError as e:check('409' in str(e));return 'conflict'
 with concurrent.futures.ThreadPoolExecutor(2) as pool:r=list(pool.map(run,ts))
 check(sorted(r)==['conflict','已接受'])
test('并发接受只能成功一次',concurrent_accept)
def profile():
 u=data('GET','/api/v1/me',token=b);v=data('PATCH','/api/v1/me',{'nickname':'新昵称','contact':'new@example.test','version':u['version']},b);check(v['nickname']=='新昵称')
test('U03 资料修改',profile)
def profile_version():
 u=data('GET','/api/v1/me',token=c);body={'nickname':'版本校验','contact':'c@example.test','version':u['version']}
 data('PATCH','/api/v1/me',body,c);call('PATCH','/api/v1/me',body,c,expected=409)
test('U03 资料旧版本被拒绝',profile_version)
test('非法分页被拦截',lambda:call('GET','/api/v1/products?page=0',expected=422))
test('数据库读写回滚探测',lambda:check(data('GET','/ready')['database']=='read_write_ok'))
def logout():data('DELETE','/api/v1/session',token=b);call('GET','/api/v1/me',token=b,expected=401)
test('退出后令牌立即失效',logout)
OUT.parent.mkdir(parents=True,exist_ok=True);OUT.write_text(json.dumps({'baseUrl':BASE,'timestamp':time.strftime('%Y-%m-%d %H:%M:%S'),'passed':sum(x['passed'] for x in results),'total':len(results),'tests':results},ensure_ascii=False,indent=2),encoding='utf-8')
sys.exit(0 if all(x['passed'] for x in results) else 1)
