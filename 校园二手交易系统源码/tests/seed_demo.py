"""通过真实接口创建虚构演示数据。先启动独立演示库的 --server，再运行本脚本。"""
import urllib.request,json,uuid,struct,zlib,base64,time,sys
BASE=sys.argv[1] if len(sys.argv)>1 else 'http://127.0.0.1:5082'
def call(method,path,body=None,token=''):
 h={'Content-Type':'application/json','X-Client-Id':'demo-seed','Idempotency-Key':uuid.uuid4().hex}
 if token:h['Authorization']='Bearer '+token
 r=urllib.request.Request(BASE+path,method=method,headers=h,data=None if method=='GET' else json.dumps(body or {},ensure_ascii=False).encode())
 with urllib.request.urlopen(r) as x:return json.load(x)['data']
for a,n in [('seller_demo','演示卖家'),('buyer_demo','演示买家'),('buyer2_demo','另一位演示买家')]:call('POST','/api/v1/users',{'account':a,'password':'Course123!','nickname':n,'contact':a+'@example.test'})
t=call('POST','/api/v1/sessions',{'account':'seller_demo','password':'Course123!'})['token']
def png(color):
 def chunk(t,b):return struct.pack('!I',len(b))+t+b+struct.pack('!I',zlib.crc32(t+b)&0xffffffff)
 return b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('!2I5B',32,32,8,2,0,0,0))+chunk(b'IDAT',zlib.compress((b'\0'+bytes(color)*32)*32))+chunk(b'IEND',b'')
for title,cat,desc,price,color in [('软件工程教材','教材','课程演示样品，少量笔记，校内当面交接。图片为纯色测试占位图，实物信息以文字描述为准。',1800,(95,140,160)),('桌面收纳盒','生活','课程演示样品，结构完整。图片为纯色测试占位图。',1200,(140,110,80)),('USB键盘','数码','课程演示样品，按键正常。图片为纯色测试占位图。',2500,(100,120,100))]:
 img=call('POST','/api/v1/images',{'fileName':'demo.png','mimeType':'image/png','contentBase64':base64.b64encode(png(color)).decode()},t)['imageId']
 call('POST','/api/v1/products',{'title':title,'category':cat,'description':desc,'priceCents':price,'imageIds':[img]},t)
time.sleep(1)
print('Created 3 demo accounts and 3 products. All data is fictitious.')
