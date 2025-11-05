—F
b/Users/oleksandrmelnychenko/RiderProjects/ecliptix-desktop/Ecliptix.Opaque.Protocol/OpaqueTypes.cs
	namespace 	
Ecliptix
 
. 
Opaque 
. 
Protocol "
;" #
public 
static 
class 
OpaqueConstants #
{ 
public 

const 
int 
PUBLIC_KEY_LENGTH &
=' (
$num) +
;+ ,
public 

const 
int 
HASH_LENGTH  
=! "
$num# %
;% &
public 

const 
int 
MASTER_KEY_LENGTH &
=' (
$num) +
;+ ,
public 

const 
int '
REGISTRATION_REQUEST_LENGTH 0
=1 2
$num3 5
;5 6
public		 

const		 
int		 (
REGISTRATION_RESPONSE_LENGTH		 1
=		2 3
$num		4 6
;		6 7
public

 

const

 
int

 &
REGISTRATION_RECORD_LENGTH

 /
=

0 1
$num

2 5
;

5 6
public 

const 
int 

KE1_LENGTH 
=  !
$num" $
;$ %
public 

const 
int 

KE2_LENGTH 
=  !
$num" %
;% &
public 

const 
int 

KE3_LENGTH 
=  !
$num" $
;$ %
} 
public 
enum 
OpaqueResult 
{ 
SUCCESS 
= 
$num 
, 
INVALID_INPUT 
= 
- 
$num 
, 
CRYPTO_ERROR 
= 
- 
$num 
, 
MEMORY_ERROR 
= 
- 
$num 
, 
VALIDATION_ERROR 
= 
- 
$num 
,  
AUTHENTICATION_ERROR 
= 
- 
$num 
, 
INVALID_PUBLIC_KEY 
= 
- 
$num 
} 
public 
sealed 
class 
RegistrationResult &
:' (
IDisposable) 4
{ 
private 
readonly 
byte 
[ 
] 
_request $
;$ %
private 
bool 
	_disposed 
; 
public   

byte   
[   
]   
GetRequestCopy    
(    !
)  ! "
=>  # %
(  & '
byte  ' +
[  + ,
]  , -
)  - .
_request  . 6
.  6 7
Clone  7 <
(  < =
)  = >
;  > ?
internal"" 
IntPtr"" 
StateHandle"" 
{""  !
get""" %
;""% &
}""' (
internal$$ 
RegistrationResult$$ 
($$  
byte$$  $
[$$$ %
]$$% &
request$$' .
,$$. /
IntPtr$$0 6
stateHandle$$7 B
)$$B C
{%% 
_request&& 
=&& 
request&& 
;&& 
StateHandle'' 
='' 
stateHandle'' !
;''! "
}(( 
public** 

void** 
Dispose** 
(** 
)** 
{++ 
Dispose,, 
(,, 
true,, 
),, 
;,, 
GC-- 

.--
 
SuppressFinalize-- 
(-- 
this--  
)--  !
;--! "
}.. 
private00 
void00 
Dispose00 
(00 
bool00 
	disposing00 '
)00' (
{11 
if22 

(22 
	_disposed22 
)22 
{33 	
return44 
;44 
}55 	
if77 

(77 
StateHandle77 
!=77 
IntPtr77 !
.77! "
Zero77" &
)77& '
{88 	
NativeLibraries99 
.99 
OpaqueNative99 (
.99( )'
opaque_client_state_destroy99) D
(99D E
StateHandle99E P
)99P Q
;99Q R
}:: 	
if<< 

(<< 
	disposing<< 
)<< 
{== 	
}?? 	
	_disposedAA 
=AA 
trueAA 
;AA 
}BB 
~DD 
RegistrationResultDD 
(DD 
)DD 
{EE 
DisposeFF 
(FF 
falseFF 
)FF 
;FF 
}GG 
}HH 
publicJJ 
sealedJJ 
classJJ 
KeyExchangeResultJJ %
:JJ& '
IDisposableJJ( 3
{KK 
privateLL 
readonlyLL 
byteLL 
[LL 
]LL 
_keyExchangeDataLL ,
;LL, -
privateMM 
readonlyMM 
IntPtrMM 
_stateHandleMM (
;MM( )
privateNN 
boolNN 
	_disposedNN 
;NN 
publicPP 

bytePP 
[PP 
]PP "
GetKeyExchangeDataCopyPP (
(PP( )
)PP) *
=>PP+ -
(PP. /
bytePP/ 3
[PP3 4
]PP4 5
)PP5 6
_keyExchangeDataPP6 F
.PPF G
ClonePPG L
(PPL M
)PPM N
;PPN O
internalRR 
IntPtrRR 
StateHandleRR 
=>RR  "
_stateHandleRR# /
;RR/ 0
internalTT 
KeyExchangeResultTT 
(TT 
byteTT #
[TT# $
]TT$ %
keyExchangeDataTT& 5
,TT5 6
IntPtrTT7 =
stateHandleTT> I
)TTI J
{UU 
_keyExchangeDataVV 
=VV 
keyExchangeDataVV *
;VV* +
_stateHandleWW 
=WW 
stateHandleWW "
;WW" #
}XX 
publicZZ 

voidZZ 
DisposeZZ 
(ZZ 
)ZZ 
{[[ 
Dispose\\ 
(\\ 
true\\ 
)\\ 
;\\ 
GC]] 

.]]
 
SuppressFinalize]] 
(]] 
this]]  
)]]  !
;]]! "
}^^ 
private`` 
void`` 
Dispose`` 
(`` 
bool`` 
	disposing`` '
)``' (
{aa 
ifbb 

(bb 
	_disposedbb 
)bb 
{cc 	
returndd 
;dd 
}ee 	
ifgg 

(gg 
StateHandlegg 
!=gg 
IntPtrgg !
.gg! "
Zerogg" &
)gg& '
{hh 	
NativeLibrariesii 
.ii 
OpaqueNativeii (
.ii( )'
opaque_client_state_destroyii) D
(iiD E
StateHandleiiE P
)iiP Q
;iiQ R
}jj 	
ifll 

(ll 
	disposingll 
)ll 
{mm 	
}oo 	
	_disposedqq 
=qq 
trueqq 
;qq 
}rr 
~tt 
KeyExchangeResulttt 
(tt 
)tt 
{uu 
Disposevv 
(vv 
falsevv 
)vv 
;vv 
}ww 
}xx 
publiczz 
staticzz 
classzz 
OpaqueErrorMessageszz '
{{{ 
public|| 

const|| 
string|| *
SERVER_PUBLIC_KEY_INVALID_SIZE|| 6
=||7 8
$str||9 f
;||f g
public}} 

const}} 
string}} *
FAILED_TO_CREATE_OPAQUE_CLIENT}} 6
=}}7 8
$str}}9 ^
;}}^ _
public~~ 

const~~ 
string~~ $
SECURE_KEY_NULL_OR_EMPTY~~ 0
=~~1 2
$str~~3 V
;~~V W
public 

const 
string "
FAILED_TO_CREATE_STATE .
=/ 0
$str1 N
;N O
public
ÄÄ 

const
ÄÄ 
string
ÄÄ 3
%FAILED_TO_CREATE_REGISTRATION_REQUEST
ÄÄ =
=
ÄÄ> ?
$str
ÄÄ@ l
;
ÄÄl m
public
ÅÅ 

const
ÅÅ 
string
ÅÅ *
SERVER_RESPONSE_INVALID_SIZE
ÅÅ 4
=
ÅÅ5 6
$str
ÅÅ7 b
;
ÅÅb c
public
ÇÇ 

const
ÇÇ 
string
ÇÇ -
FAILED_TO_FINALIZE_REGISTRATION
ÇÇ 7
=
ÇÇ8 9
$str
ÇÇ: `
;
ÇÇ` a
public
ÉÉ 

const
ÉÉ 
string
ÉÉ $
FAILED_TO_GENERATE_KE1
ÉÉ .
=
ÉÉ/ 0
$str
ÉÉ1 N
;
ÉÉN O
public
ÑÑ 

const
ÑÑ 
string
ÑÑ *
FAILED_TO_DERIVE_SESSION_KEY
ÑÑ 4
=
ÑÑ5 6
$str
ÑÑ7 Z
;
ÑÑZ [
}ÖÖ Ôô
c/Users/oleksandrmelnychenko/RiderProjects/ecliptix-desktop/Ecliptix.Opaque.Protocol/OpaqueClient.cs
	namespace 	
Ecliptix
 
. 
Opaque 
. 
Protocol "
;" #
public 
sealed 
class 
OpaqueClient  
:! "
IDisposable# .
{ 
private		 
readonly		 
IntPtr		 
_clientHandle		 )
;		) *
private

 
bool

 
	_disposed

 
;

 
public 

OpaqueClient 
( 
byte 
[ 
] 
serverPublicKey .
). /
{ 
if 

( 
serverPublicKey 
? 
. 
Length #
!=$ &
OpaqueConstants' 6
.6 7
PUBLIC_KEY_LENGTH7 H
)H I
{ 	
throw 
new 
ArgumentException '
(' (
string( .
.. /
Format/ 5
(5 6
OpaqueErrorMessages6 I
.I J*
SERVER_PUBLIC_KEY_INVALID_SIZEJ h
,h i
OpaqueConstantsj y
.y z
PUBLIC_KEY_LENGTH	z ã
)
ã å
)
å ç
;
ç é
} 	
int 
result 
= 
OpaqueNative 
.  
opaque_client_create -
(- .
serverPublicKey. =
,= >
(? @
UIntPtr@ G
)G H
serverPublicKeyH W
.W X
LengthX ^
,^ _
out` c
_clientHandled q
)q r
;r s
if 

( 
result 
!= 
( 
int 
) 
OpaqueResult '
.' (
SUCCESS( /
||0 2
_clientHandle3 @
==A C
IntPtrD J
.J K
ZeroK O
)O P
{ 	
throw 
new %
InvalidOperationException /
(/ 0
string0 6
.6 7
Format7 =
(= >
OpaqueErrorMessages> Q
.Q R*
FAILED_TO_CREATE_OPAQUE_CLIENTR p
,p q
(r s
OpaqueResults 
)	 Ä
result
Ä Ü
)
Ü á
)
á à
;
à â
} 	
} 
public 

RegistrationResult %
CreateRegistrationRequest 7
(7 8
byte8 <
[< =
]= >
	secureKey? H
)H I
{ 
ThrowIfDisposed 
( 
) 
; 
if 

( 
	secureKey 
== 
null 
||  
	secureKey! *
.* +
Length+ 1
==2 4
$num5 6
)6 7
{ 	
throw   
new   
ArgumentException   '
(  ' (
OpaqueErrorMessages  ( ;
.  ; <$
SECURE_KEY_NULL_OR_EMPTY  < T
)  T U
;  U V
}!! 	
try## 
{$$ 	
byte%% 
[%% 
]%% 
request%% 
=%% 
new%%  
byte%%! %
[%%% &
OpaqueConstants%%& 5
.%%5 6'
REGISTRATION_REQUEST_LENGTH%%6 Q
]%%Q R
;%%R S
int'' 
stateResult'' 
='' 
OpaqueNative'' *
.''* +&
opaque_client_state_create''+ E
(''E F
out''F I
IntPtr''J P
state''Q V
)''V W
;''W X
if(( 
((( 
stateResult(( 
!=(( 
(((  
int((  #
)((# $
OpaqueResult(($ 0
.((0 1
SUCCESS((1 8
)((8 9
{)) 
throw** 
new** %
InvalidOperationException** 3
(**3 4
string**4 :
.**: ;
Format**; A
(**A B
OpaqueErrorMessages**B U
.**U V"
FAILED_TO_CREATE_STATE**V l
,**l m
(**n o
OpaqueResult**o {
)**{ |
stateResult	**| á
)
**á à
)
**à â
;
**â ä
}++ 
int-- 
result-- 
=-- 
OpaqueNative-- %
.--% &5
)opaque_client_create_registration_request--& O
(--O P
_clientHandle.. 
,.. 
	secureKey.. (
,..( )
(..* +
UIntPtr..+ 2
)..2 3
	secureKey..3 <
...< =
Length..= C
,..C D
state..E J
,..J K
request..L S
,..S T
(..U V
UIntPtr..V ]
)..] ^
request..^ e
...e f
Length..f l
)..l m
;..m n
if00 
(00 
result00 
!=00 
(00 
int00 
)00 
OpaqueResult00 +
.00+ ,
SUCCESS00, 3
)003 4
{11 
OpaqueNative22 
.22 '
opaque_client_state_destroy22 8
(228 9
state229 >
)22> ?
;22? @
throw33 
new33 %
InvalidOperationException33 3
(333 4
string334 :
.33: ;
Format33; A
(33A B
OpaqueErrorMessages33B U
.33U V1
%FAILED_TO_CREATE_REGISTRATION_REQUEST33V {
,33{ |
(33} ~
OpaqueResult	33~ ä
)
33ä ã
result
33ã ë
)
33ë í
)
33í ì
;
33ì î
}44 
return66 
new66 
RegistrationResult66 )
(66) *
request66* 1
,661 2
state663 8
)668 9
;669 :
}77 	
finally88 
{99 	
ClearSecureKey:: 
(:: 
	secureKey:: $
)::$ %
;::% &
};; 	
}<< 
public>> 

(>> 
byte>> 
[>> 
]>> 
Record>> 
,>> 
byte>> 
[>>  
]>>  !
	MasterKey>>" +
)>>+ , 
FinalizeRegistration>>- A
(>>A B
byte>>B F
[>>F G
]>>G H
serverResponse>>I W
,>>W X
RegistrationResult>>Y k
registrationState>>l }
)>>} ~
{?? 
ThrowIfDisposed@@ 
(@@ 
)@@ 
;@@ 
ifAA 

(AA 
serverResponseAA 
?AA 
.AA 
LengthAA "
!=AA# %
OpaqueConstantsAA& 5
.AA5 6(
REGISTRATION_RESPONSE_LENGTHAA6 R
)AAR S
{BB 	
throwCC 
newCC 
ArgumentExceptionCC '
(CC' (
stringDD 
.DD 
FormatDD 
(DD 
OpaqueErrorMessagesDD 1
.DD1 2(
SERVER_RESPONSE_INVALID_SIZEDD2 N
,DDN O
OpaqueConstantsDDP _
.DD_ `(
REGISTRATION_RESPONSE_LENGTHDD` |
)DD| }
)DD} ~
;DD~ 
}EE 	
byteGG 
[GG 
]GG 
	masterKeyGG 
=GG 
newGG 
byteGG #
[GG# $
OpaqueConstantsGG$ 3
.GG3 4
MASTER_KEY_LENGTHGG4 E
]GGE F
;GGF G
SystemHH 
.HH 
SecurityHH 
.HH 
CryptographyHH $
.HH$ %!
RandomNumberGeneratorHH% :
.HH: ;
FillHH; ?
(HH? @
	masterKeyHH@ I
)HHI J
;HHJ K
byteJJ 
[JJ 
]JJ 
recordJJ 
=JJ 
newJJ 
byteJJ  
[JJ  !
OpaqueConstantsJJ! 0
.JJ0 1&
REGISTRATION_RECORD_LENGTHJJ1 K
]JJK L
;JJL M
intLL 
resultLL 
=LL 
OpaqueNativeLL !
.LL! "/
#opaque_client_finalize_registrationLL" E
(LLE F
_clientHandleMM 
,MM 
serverResponseMM )
,MM) *
(MM+ ,
UIntPtrMM, 3
)MM3 4
serverResponseMM4 B
.MMB C
LengthMMC I
,MMI J
	masterKeyNN 
,NN 
(NN 
UIntPtrNN 
)NN  
	masterKeyNN  )
.NN) *
LengthNN* 0
,NN0 1
registrationStateOO 
.OO 
StateHandleOO )
,OO) *
recordOO+ 1
,OO1 2
(OO3 4
UIntPtrOO4 ;
)OO; <
recordOO< B
.OOB C
LengthOOC I
)OOI J
;OOJ K
ifQQ 

(QQ 
resultQQ 
!=QQ 
(QQ 
intQQ 
)QQ 
OpaqueResultQQ '
.QQ' (
SUCCESSQQ( /
)QQ/ 0
{RR 	
throwSS 
newSS %
InvalidOperationExceptionSS /
(SS/ 0
stringSS0 6
.SS6 7
FormatSS7 =
(SS= >
OpaqueErrorMessagesSS> Q
.SSQ R+
FAILED_TO_FINALIZE_REGISTRATIONSSR q
,SSq r
(SSs t
OpaqueResult	SSt Ä
)
SSÄ Å
result
SSÅ á
)
SSá à
)
SSà â
;
SSâ ä
}TT 	
returnVV 
(VV 
recordVV 
,VV 
	masterKeyVV !
)VV! "
;VV" #
}WW 
publicYY 

KeyExchangeResultYY 
GenerateKE1YY (
(YY( )
byteYY) -
[YY- .
]YY. /
	secureKeyYY0 9
)YY9 :
{ZZ 
ThrowIfDisposed[[ 
([[ 
)[[ 
;[[ 
if\\ 

(\\ 
	secureKey\\ 
==\\ 
null\\ 
||\\  
	secureKey\\! *
.\\* +
Length\\+ 1
==\\2 4
$num\\5 6
)\\6 7
{]] 	
throw^^ 
new^^ 
ArgumentException^^ '
(^^' (
OpaqueErrorMessages^^( ;
.^^; <$
SECURE_KEY_NULL_OR_EMPTY^^< T
)^^T U
;^^U V
}__ 	
tryaa 
{bb 	
bytecc 
[cc 
]cc 
ke1cc 
=cc 
newcc 
bytecc !
[cc! "
OpaqueConstantscc" 1
.cc1 2

KE1_LENGTHcc2 <
]cc< =
;cc= >
intee 
stateResultee 
=ee 
OpaqueNativeee *
.ee* +&
opaque_client_state_createee+ E
(eeE F
outeeF I
IntPtreeJ P
stateeeQ V
)eeV W
;eeW X
ifff 
(ff 
stateResultff 
!=ff 
(ff  
intff  #
)ff# $
OpaqueResultff$ 0
.ff0 1
SUCCESSff1 8
)ff8 9
{gg 
throwhh 
newhh %
InvalidOperationExceptionhh 3
(hh3 4
stringhh4 :
.hh: ;
Formathh; A
(hhA B
OpaqueErrorMessageshhB U
.hhU V"
FAILED_TO_CREATE_STATEhhV l
,hhl m
(hhn o
OpaqueResulthho {
)hh{ |
stateResult	hh| á
)
hhá à
)
hhà â
;
hhâ ä
}ii 
intkk 
resultkk 
=kk 
OpaqueNativekk %
.kk% &&
opaque_client_generate_ke1kk& @
(kk@ A
_clientHandlell 
,ll 
	secureKeyll (
,ll( )
(ll* +
UIntPtrll+ 2
)ll2 3
	secureKeyll3 <
.ll< =
Lengthll= C
,llC D
statellE J
,llJ K
ke1llL O
,llO P
(llQ R
UIntPtrllR Y
)llY Z
ke1llZ ]
.ll] ^
Lengthll^ d
)lld e
;lle f
ifnn 
(nn 
resultnn 
!=nn 
(nn 
intnn 
)nn 
OpaqueResultnn +
.nn+ ,
SUCCESSnn, 3
)nn3 4
{oo 
OpaqueNativepp 
.pp '
opaque_client_state_destroypp 8
(pp8 9
statepp9 >
)pp> ?
;pp? @
throwqq 
newqq %
InvalidOperationExceptionqq 3
(qq3 4
stringqq4 :
.qq: ;
Formatqq; A
(qqA B
OpaqueErrorMessagesqqB U
.qqU V"
FAILED_TO_GENERATE_KE1qqV l
,qql m
(qqn o
OpaqueResultqqo {
)qq{ |
result	qq| Ç
)
qqÇ É
)
qqÉ Ñ
;
qqÑ Ö
}rr 
returntt 
newtt 
KeyExchangeResulttt (
(tt( )
ke1tt) ,
,tt, -
statett. 3
)tt3 4
;tt4 5
}uu 	
finallyvv 
{ww 	
ClearSecureKeyxx 
(xx 
	secureKeyxx $
)xx$ %
;xx% &
}yy 	
}zz 
public|| 

Result|| 
<|| 
byte|| 
[|| 
]|| 
,|| 
OpaqueResult|| &
>||& '
GenerateKe3||( 3
(||3 4
byte||4 8
[||8 9
]||9 :
?||: ;
ke2||< ?
,||? @
KeyExchangeResult||A R
keyExchangeState||S c
)||c d
{}} 
ThrowIfDisposed~~ 
(~~ 
)~~ 
;~~ 
if 

( 
ke2 
? 
. 
Length 
!= 
OpaqueConstants *
.* +

KE2_LENGTH+ 5
)5 6
{
ÄÄ 	
return
ÅÅ 
Result
ÅÅ 
<
ÅÅ 
byte
ÅÅ 
[
ÅÅ 
]
ÅÅ  
,
ÅÅ  !
OpaqueResult
ÅÅ" .
>
ÅÅ. /
.
ÅÅ/ 0
Err
ÅÅ0 3
(
ÅÅ3 4
OpaqueResult
ÅÅ4 @
.
ÅÅ@ A
INVALID_INPUT
ÅÅA N
)
ÅÅN O
;
ÅÅO P
}
ÇÇ 	
byte
ÑÑ 
[
ÑÑ 
]
ÑÑ 
ke3
ÑÑ 
=
ÑÑ 
new
ÑÑ 
byte
ÑÑ 
[
ÑÑ 
OpaqueConstants
ÑÑ -
.
ÑÑ- .

KE3_LENGTH
ÑÑ. 8
]
ÑÑ8 9
;
ÑÑ9 :
int
ÜÜ 
result
ÜÜ 
=
ÜÜ 
OpaqueNative
ÜÜ !
.
ÜÜ! "(
opaque_client_generate_ke3
ÜÜ" <
(
ÜÜ< =
_clientHandle
áá 
,
áá 
ke2
áá 
,
áá 
(
áá  !
UIntPtr
áá! (
)
áá( )
ke2
áá) ,
.
áá, -
Length
áá- 3
,
áá3 4
keyExchangeState
áá5 E
.
ááE F
StateHandle
ááF Q
,
ááQ R
ke3
ááS V
,
ááV W
(
ááX Y
UIntPtr
ááY `
)
áá` a
ke3
ááa d
.
áád e
Length
ááe k
)
áák l
;
áál m
return
ââ 
result
ââ 
!=
ââ 
(
ââ 
int
ââ 
)
ââ 
OpaqueResult
ââ *
.
ââ* +
SUCCESS
ââ+ 2
?
ää 
Result
ää 
<
ää 
byte
ää 
[
ää 
]
ää 
,
ää 
OpaqueResult
ää )
>
ää) *
.
ää* +
Err
ää+ .
(
ää. /
(
ää/ 0
OpaqueResult
ää0 <
)
ää< =
result
ää= C
)
ääC D
:
ãã 
Result
ãã 
<
ãã 
byte
ãã 
[
ãã 
]
ãã 
,
ãã 
OpaqueResult
ãã )
>
ãã) *
.
ãã* +
Ok
ãã+ -
(
ãã- .
ke3
ãã. 1
)
ãã1 2
;
ãã2 3
}
åå 
public
éé 

(
éé 
byte
éé 
[
éé 
]
éé 

SessionKey
éé 
,
éé 
byte
éé #
[
éé# $
]
éé$ %
	MasterKey
éé& /
)
éé/ 0!
DeriveBaseMasterKey
éé1 D
(
ééD E
KeyExchangeResult
ééE V
keyExchangeState
ééW g
)
éég h
{
èè 
ThrowIfDisposed
êê 
(
êê 
)
êê 
;
êê 
byte
íí 
[
íí 
]
íí 

sessionKey
íí 
=
íí 
new
íí 
byte
íí  $
[
íí$ %
OpaqueConstants
íí% 4
.
íí4 5
HASH_LENGTH
íí5 @
]
íí@ A
;
ííA B
byte
ìì 
[
ìì 
]
ìì 
	masterKey
ìì 
=
ìì 
new
ìì 
byte
ìì #
[
ìì# $
OpaqueConstants
ìì$ 3
.
ìì3 4
MASTER_KEY_LENGTH
ìì4 E
]
ììE F
;
ììF G
int
ïï 
result
ïï 
=
ïï 
OpaqueNative
ïï !
.
ïï! ""
opaque_client_finish
ïï" 6
(
ïï6 7
_clientHandle
ññ 
,
ññ 
keyExchangeState
ññ +
.
ññ+ ,
StateHandle
ññ, 7
,
ññ7 8

sessionKey
óó 
,
óó 
(
óó 
UIntPtr
óó  
)
óó  !

sessionKey
óó! +
.
óó+ ,
Length
óó, 2
,
óó2 3
	masterKey
òò 
,
òò 
(
òò 
UIntPtr
òò 
)
òò  
	masterKey
òò  )
.
òò) *
Length
òò* 0
)
òò0 1
;
òò1 2
if
öö 

(
öö 
result
öö 
!=
öö 
(
öö 
int
öö 
)
öö 
OpaqueResult
öö '
.
öö' (
SUCCESS
öö( /
)
öö/ 0
{
õõ 	
throw
úú 
new
úú '
InvalidOperationException
úú /
(
úú/ 0
string
úú0 6
.
úú6 7
Format
úú7 =
(
úú= >!
OpaqueErrorMessages
úú> Q
.
úúQ R*
FAILED_TO_DERIVE_SESSION_KEY
úúR n
,
úún o
(
úúp q
OpaqueResult
úúq }
)
úú} ~
resultúú~ Ñ
)úúÑ Ö
)úúÖ Ü
;úúÜ á
}
ùù 	
return
üü 
(
üü 

sessionKey
üü 
,
üü 
	masterKey
üü %
)
üü% &
;
üü& '
}
†† 
private
¢¢ 
void
¢¢ 
ThrowIfDisposed
¢¢  
(
¢¢  !
)
¢¢! "
{
££ 
if
§§ 

(
§§ 
	_disposed
§§ 
)
§§ 
{
•• 	
throw
¶¶ 
new
¶¶ %
ObjectDisposedException
¶¶ -
(
¶¶- .
nameof
¶¶. 4
(
¶¶4 5
OpaqueClient
¶¶5 A
)
¶¶A B
)
¶¶B C
;
¶¶C D
}
ßß 	
}
®® 
private
™™ 
static
™™ 
void
™™ 
ClearSecureKey
™™ &
(
™™& '
byte
™™' +
[
™™+ ,
]
™™, -
	secureKey
™™. 7
)
™™7 8
{
´´ %
CryptographicOperations
¨¨ 
.
¨¨  

ZeroMemory
¨¨  *
(
¨¨* +
	secureKey
¨¨+ 4
)
¨¨4 5
;
¨¨5 6
}
≠≠ 
public
ØØ 

void
ØØ 
Dispose
ØØ 
(
ØØ 
)
ØØ 
{
∞∞ 
if
±± 

(
±± 
!
±± 
	_disposed
±± 
&&
±± 
_clientHandle
±± '
!=
±±( *
IntPtr
±±+ 1
.
±±1 2
Zero
±±2 6
)
±±6 7
{
≤≤ 	
OpaqueNative
≥≥ 
.
≥≥ #
opaque_client_destroy
≥≥ .
(
≥≥. /
_clientHandle
≥≥/ <
)
≥≥< =
;
≥≥= >
	_disposed
¥¥ 
=
¥¥ 
true
¥¥ 
;
¥¥ 
}
µµ 	
}
∂∂ 
}∑∑ ÓM
s/Users/oleksandrmelnychenko/RiderProjects/ecliptix-desktop/Ecliptix.Opaque.Protocol/NativeLibraries/OpaqueNative.cs
	namespace 	
Ecliptix
 
. 
Opaque 
. 
Protocol "
." #
NativeLibraries# 2
;2 3
internal 
static	 
partial 
class 
OpaqueNative *
{ 
private 
const 
string 
LIBRARY  
=! "
$str# 5
;5 6
static

 

OpaqueNative

 
(

 
)

 
{ 
NativeLibrary 
.  
SetDllImportResolver *
(* +
typeof+ 1
(1 2
OpaqueNative2 >
)> ?
.? @
Assembly@ H
,H I
DllImportResolverJ [
)[ \
;\ ]
} 
private 
static 
IntPtr 
DllImportResolver +
(+ ,
string, 2
libraryName3 >
,> ?
System@ F
.F G

ReflectionG Q
.Q R
AssemblyR Z
assembly[ c
,c d
DllImportSearchPathe x
?x y

searchPath	z Ñ
)
Ñ Ö
{ 
if 

( 
libraryName 
!= 
LIBRARY "
)" #
{ 	
return 
IntPtr 
. 
Zero 
; 
} 	
string 
platformLibrary 
; 
if 

( 
RuntimeInformation 
. 
IsOSPlatform +
(+ ,

OSPlatform, 6
.6 7
Windows7 >
)> ?
)? @
{ 	
platformLibrary 
= 
$str 4
;4 5
} 	
else 
if 
( 
RuntimeInformation #
.# $
IsOSPlatform$ 0
(0 1

OSPlatform1 ;
.; <
OSX< ?
)? @
)@ A
{ 	
platformLibrary 
= 
$str 6
;6 7
} 	
else 
{   	
platformLibrary!! 
=!! 
$str!! 3
;!!3 4
}"" 	
if$$ 

($$ 
NativeLibrary$$ 
.$$ 
TryLoad$$ !
($$! "
platformLibrary$$" 1
,$$1 2
assembly$$3 ;
,$$; <

searchPath$$= G
,$$G H
out$$I L
IntPtr$$M S
handle$$T Z
)$$Z [
)$$[ \
{%% 	
return&& 
handle&& 
;&& 
}'' 	
return(( 
IntPtr(( 
.(( 
Zero(( 
;(( 
})) 
[++ 
LibraryImport++ 
(++ 
LIBRARY++ 
,++ 

EntryPoint++ &
=++' (
$str++) ?
)++? @
]++@ A
[,, 
UnmanagedCallConv,, 
(,, 
	CallConvs,,  
=,,! "
new,,# &
[,,& '
],,' (
{,,) *
typeof,,+ 1
(,,1 2
CallConvCdecl,,2 ?
),,? @
},,A B
),,B C
],,C D
internal-- 
static-- 
partial-- 
int--  
opaque_client_create--  4
(--4 5
byte--5 9
[--9 :
]--: ;
serverPublicKey--< K
,--K L
nuint--M R
	keyLength--S \
,--\ ]
out--^ a
IntPtr--b h
handle--i o
)--o p
;--p q
[// 
LibraryImport// 
(// 
LIBRARY// 
,// 

EntryPoint// &
=//' (
$str//) @
)//@ A
]//A B
[00 
UnmanagedCallConv00 
(00 
	CallConvs00  
=00! "
new00# &
[00& '
]00' (
{00) *
typeof00+ 1
(001 2
CallConvCdecl002 ?
)00? @
}00A B
)00B C
]00C D
internal11 
static11 
partial11 
void11  !
opaque_client_destroy11! 6
(116 7
IntPtr117 =
handle11> D
)11D E
;11E F
[33 
LibraryImport33 
(33 
LIBRARY33 
,33 

EntryPoint33 &
=33' (
$str33) E
)33E F
]33F G
[44 
UnmanagedCallConv44 
(44 
	CallConvs44  
=44! "
new44# &
[44& '
]44' (
{44) *
typeof44+ 1
(441 2
CallConvCdecl442 ?
)44? @
}44A B
)44B C
]44C D
internal55 
static55 
partial55 
int55 &
opaque_client_state_create55  :
(55: ;
out55; >
IntPtr55? E
handle55F L
)55L M
;55M N
[77 
LibraryImport77 
(77 
LIBRARY77 
,77 

EntryPoint77 &
=77' (
$str77) F
)77F G
]77G H
[88 
UnmanagedCallConv88 
(88 
	CallConvs88  
=88! "
new88# &
[88& '
]88' (
{88) *
typeof88+ 1
(881 2
CallConvCdecl882 ?
)88? @
}88A B
)88B C
]88C D
internal99 
static99 
partial99 
void99  '
opaque_client_state_destroy99! <
(99< =
IntPtr99= C
handle99D J
)99J K
;99K L
[;; 
	DllImport;; 
(;; 
LIBRARY;; 
,;; 
CallingConvention;; )
=;;* +
CallingConvention;;, =
.;;= >
Cdecl;;> C
);;C D
];;D E
internal<< 
static<< 
extern<< 
int<< 5
)opaque_client_create_registration_request<< H
(<<H I
IntPtr== 
clientHandle== 
,== 
byte== !
[==! "
]==" #
password==$ ,
,==, -
UIntPtr==. 5
passwordLength==6 D
,==D E
IntPtr==F L
stateHandle==M X
,==X Y
byte==Z ^
[==^ _
]==_ `
requestData==a l
,==l m
UIntPtr==n u
requestBufferSize	==v á
)
==á à
;
==à â
[?? 
	DllImport?? 
(?? 
LIBRARY?? 
,?? 
CallingConvention?? )
=??* +
CallingConvention??, =
.??= >
Cdecl??> C
)??C D
]??D E
internal@@ 
static@@ 
extern@@ 
int@@ /
#opaque_client_finalize_registration@@ B
(@@B C
IntPtrAA 
clientHandleAA 
,AA 
byteAA !
[AA! "
]AA" #
responseDataAA$ 0
,AA0 1
UIntPtrAA2 9
responseLengthAA: H
,AAH I
byteAAJ N
[AAN O
]AAO P
	masterKeyAAQ Z
,AAZ [
UIntPtrAA\ c
masterKeyLengthAAd s
,AAs t
IntPtrAAu {
stateHandle	AA| á
,
AAá à
byte
AAâ ç
[
AAç é
]
AAé è

recordData
AAê ö
,
AAö õ
UIntPtr
AAú £
recordBufferSize
AA§ ¥
)
AA¥ µ
;
AAµ ∂
[CC 
	DllImportCC 
(CC 
LIBRARYCC 
,CC 
CallingConventionCC )
=CC* +
CallingConventionCC, =
.CC= >
CdeclCC> C
)CCC D
]CCD E
internalDD 
staticDD 
externDD 
intDD &
opaque_client_generate_ke1DD 9
(DD9 :
IntPtrEE 
clientHandleEE 
,EE 
byteEE !
[EE! "
]EE" #
passwordEE$ ,
,EE, -
UIntPtrEE. 5
passwordLengthEE6 D
,EED E
IntPtrEEF L
stateHandleEEM X
,EEX Y
byteEEZ ^
[EE^ _
]EE_ `
ke1DataEEa h
,EEh i
UIntPtrEEj q
ke1BufferSizeEEr 
)	EE Ä
;
EEÄ Å
[GG 
	DllImportGG 
(GG 
LIBRARYGG 
,GG 
CallingConventionGG )
=GG* +
CallingConventionGG, =
.GG= >
CdeclGG> C
)GGC D
]GGD E
internalHH 
staticHH 
externHH 
intHH &
opaque_client_generate_ke3HH 9
(HH9 :
IntPtrII 
clientHandleII 
,II 
byteII !
[II! "
]II" #
ke2DataII$ +
,II+ ,
UIntPtrII- 4
	ke2LengthII5 >
,II> ?
IntPtrII@ F
stateHandleIIG R
,IIR S
byteIIT X
[IIX Y
]IIY Z
ke3DataII[ b
,IIb c
UIntPtrIId k
ke3BufferSizeIIl y
)IIy z
;IIz {
[KK 
	DllImportKK 
(KK 
LIBRARYKK 
,KK 
CallingConventionKK )
=KK* +
CallingConventionKK, =
.KK= >
CdeclKK> C
)KKC D
]KKD E
internalLL 
staticLL 
externLL 
intLL  
opaque_client_finishLL 3
(LL3 4
IntPtrMM 
clientHandleMM 
,MM 
IntPtrMM #
stateHandleMM$ /
,MM/ 0
byteMM1 5
[MM5 6
]MM6 7

sessionKeyMM8 B
,MMB C
UIntPtrMMD K 
sessionKeyBufferSizeMML `
,MM` a
byteMMb f
[MMf g
]MMg h
	masterKeyMMi r
,MMr s
UIntPtrMMt { 
masterKeyBufferSize	MM| è
)
MMè ê
;
MMê ë
}NN 