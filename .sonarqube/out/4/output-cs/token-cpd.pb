‰4
ç/Users/oleksandrmelnychenko/RiderProjects/ecliptix-desktop/Ecliptix.Security.Certificate.Pinning/Services/CertificatePinningServiceFactory.cs
	namespace 	
Ecliptix
 
. 
Security 
. 
Certificate '
.' (
Pinning( /
./ 0
Services0 8
;8 9
public 
sealed 
class ,
 CertificatePinningServiceFactory 4
:5 6-
!ICertificatePinningServiceFactory7 X
{ 
private 
Option 
< %
CertificatePinningService ,
>, -
_service. 6
=7 8
Option9 ?
<? @%
CertificatePinningService@ Y
>Y Z
.Z [
None[ _
;_ `
private		 
readonly		 
SemaphoreSlim		 "$
_initializationSemaphore		# ;
=		< =
new		> A
(		A B
$num		B C
,		C D
$num		E F
)		F G
;		G H
private 
bool 
	_disposed 
; 
public 

async 
Task 
< 
Option 
< %
CertificatePinningService 6
>6 7
>7 8'
GetOrInitializeServiceAsync9 T
(T U
)U V
{ 
if 

( 
	_disposed 
) 
{ 	
return 
Option 
< %
CertificatePinningService 3
>3 4
.4 5
None5 9
;9 :
} 	
if 

( 
_service 
. 
IsSome 
) 
{ 	
return 
_service 
; 
} 	
await $
_initializationSemaphore &
.& '
	WaitAsync' 0
(0 1
)1 2
;2 3
try 
{ 	
return 
	_disposed 
? 
Option %
<% &%
CertificatePinningService& ?
>? @
.@ A
NoneA E
:F G
_serviceH P
.P Q
OrQ S
(S T 
TryInitializeServiceT h
)h i
;i j
} 	
finally 
{ 	$
_initializationSemaphore   $
.  $ %
Release  % ,
(  , -
)  - .
;  . /
}!! 	
}"" 
private$$ 
Option$$ 
<$$ %
CertificatePinningService$$ ,
>$$, - 
TryInitializeService$$. B
($$B C
)$$C D
{%% %
CertificatePinningService&& !
service&&" )
=&&* +
new&&, /
(&&/ 0
)&&0 1
;&&1 2-
!CertificatePinningOperationResult'' )
result''* 0
=''1 2
service''3 :
.'': ;

Initialize''; E
(''E F
)''F G
;''G H
if)) 

()) 
result)) 
.)) 
	IsSuccess)) 
))) 
{** 	
_service++ 
=++ 
Option++ 
<++ %
CertificatePinningService++ 7
>++7 8
.++8 9
Some++9 =
(++= >
service++> E
)++E F
;++F G
	AppDomain,, 
.,, 
CurrentDomain,, #
.,,# $
ProcessExit,,$ /
+=,,0 2
OnProcessExitAsync,,3 E
;,,E F
return-- 
_service-- 
;-- 
}.. 	
service00 
.00 
DisposeAsync00 
(00 
)00 
.00 
AsTask00 %
(00% &
)00& '
.00' (

GetAwaiter00( 2
(002 3
)003 4
.004 5
	GetResult005 >
(00> ?
)00? @
;00@ A
return11 
Option11 
<11 %
CertificatePinningService11 /
>11/ 0
.110 1
None111 5
;115 6
}22 
private44 
async44 
void44 
OnProcessExitAsync44 )
(44) *
object44* 0
?440 1
sender442 8
,448 9
	EventArgs44: C
e44D E
)44E F
{55 
try66 
{77 	
await88 
DisposeAsync88 
(88 
)88  
;88  !
}99 	
catch:: 
(:: 
	Exception:: 
):: 
{;; 	
}== 	
}>> 
public@@ 

async@@ 
	ValueTask@@ 
DisposeAsync@@ '
(@@' (
)@@( )
{AA 
ifBB 

(BB 
	_disposedBB 
)BB 
{CC 	
returnDD 
;DD 
}EE 	
awaitGG $
_initializationSemaphoreGG &
.GG& '
	WaitAsyncGG' 0
(GG0 1
)GG1 2
.GG2 3
ConfigureAwaitGG3 A
(GGA B
falseGGB G
)GGG H
;GGH I
tryHH 
{II 	
ifJJ 
(JJ 
	_disposedJJ 
)JJ 
{KK 
returnLL 
;LL 
}MM 
	_disposedOO 
=OO 
trueOO 
;OO 
OptionPP 
<PP %
CertificatePinningServicePP ,
>PP, -
serviceToDisposePP. >
=PP? @
_servicePPA I
;PPI J
_serviceQQ 
=QQ 
OptionQQ 
<QQ %
CertificatePinningServiceQQ 7
>QQ7 8
.QQ8 9
NoneQQ9 =
;QQ= >
trySS 
{TT 
	AppDomainUU 
.UU 
CurrentDomainUU '
.UU' (
ProcessExitUU( 3
-=UU4 6
OnProcessExitAsyncUU7 I
;UUI J
ifWW 
(WW 
serviceToDisposeWW $
.WW$ %
IsSomeWW% +
)WW+ ,
{XX 
awaitYY 
serviceToDisposeYY *
.YY* +
ValueYY+ 0
!YY0 1
.YY1 2
DisposeAsyncYY2 >
(YY> ?
)YY? @
.YY@ A
ConfigureAwaitYYA O
(YYO P
falseYYP U
)YYU V
;YYV W
}ZZ 
}[[ 
catch\\ 
(\\ 
	Exception\\ 
)\\ 
{]] 
}__ 
}`` 	
finallyaa 
{bb 	$
_initializationSemaphorecc $
.cc$ %
Releasecc% ,
(cc, -
)cc- .
;cc. /$
_initializationSemaphoredd $
.dd$ %
Disposedd% ,
(dd, -
)dd- .
;dd. /
}ee 	
}ff 
}gg ﬂ
é/Users/oleksandrmelnychenko/RiderProjects/ecliptix-desktop/Ecliptix.Security.Certificate.Pinning/Services/ICertificatePinningServiceFactory.cs
	namespace 	
Ecliptix
 
. 
Security 
. 
Certificate '
.' (
Pinning( /
./ 0
Services0 8
;8 9
public 
	interface -
!ICertificatePinningServiceFactory 2
:3 4
IAsyncDisposable5 E
{ 
Task 
< 	
Option	 
< %
CertificatePinningService )
>) *
>* +'
GetOrInitializeServiceAsync, G
(G H
)H I
;I J
} ∞É
Ü/Users/oleksandrmelnychenko/RiderProjects/ecliptix-desktop/Ecliptix.Security.Certificate.Pinning/Services/CertificatePinningService.cs
	namespace 	
Ecliptix
 
. 
Security 
. 
Certificate '
.' (
Pinning( /
./ 0
Services0 8
;8 9
public 
sealed 
class %
CertificatePinningService -
:. /
IAsyncDisposable0 @
{		 
private

 
const

 
int

 
NOT_INITIALIZED

 %
=

& '
$num

( )
;

) *
private 
const 
int 
INITIALIZING "
=# $
$num% &
;& '
private 
const 
int 
INITIALIZED !
=" #
$num$ %
;% &
private 
const 
int 
DISPOSED 
=  
$num! "
;" #
private 
volatile 
int 
_state 
=  !
NOT_INITIALIZED" 1
;1 2
private 
readonly 
SemaphoreSlim "
_initializationLock# 6
=7 8
new9 <
(< =
$num= >
,> ?
$num@ A
)A B
;B C
public 
-
!CertificatePinningOperationResult ,

Initialize- 7
(7 8
CancellationToken8 I
cancellationTokenJ [
=\ ]
default^ e
)e f
{ 
if 

( 
_state 
== 
DISPOSED 
) 
{ 	
return -
!CertificatePinningOperationResult 4
.4 5
	FromError5 >
(> ?%
CertificatePinningFailure? X
.X Y
SERVICE_DISPOSEDY i
(i j
)j k
)k l
;l m
} 	
if 

( 
_state 
== 
INITIALIZED !
)! "
{ 	
return -
!CertificatePinningOperationResult 4
.4 5
Success5 <
(< =
)= >
;> ?
} 	
_initializationLock 
. 
Wait  
(  !
cancellationToken! 2
)2 3
;3 4
try 
{   	
if!! 
(!! 
_state!! 
==!! 
INITIALIZED!! %
)!!% &
{"" 
return## -
!CertificatePinningOperationResult## 8
.##8 9
Success##9 @
(##@ A
)##A B
;##B C
}$$ 
if&& 
(&& 
_state&& 
==&& 
DISPOSED&& "
)&&" #
{'' 
return(( -
!CertificatePinningOperationResult(( 8
.((8 9
	FromError((9 B
(((B C%
CertificatePinningFailure((C \
.((\ ]
SERVICE_DISPOSED((] m
(((m n
)((n o
)((o p
;((p q
})) 
Interlocked++ 
.++ 
Exchange++  
(++  !
ref++! $
_state++% +
,+++ ,
INITIALIZING++- 9
)++9 :
;++: ;-
!CertificatePinningOperationResult-- -
result--. 4
=--5 6
InitializeCore--7 E
(--E F
)--F G
;--G H
Interlocked// 
.// 
Exchange//  
(//  !
ref//! $
_state//% +
,//+ ,
result//- 3
.//3 4
	IsSuccess//4 =
?//> ?
INITIALIZED//@ K
://L M
NOT_INITIALIZED//N ]
)//] ^
;//^ _
return00 
result00 
;00 
}11 	
finally22 
{33 	
_initializationLock44 
.44  
Release44  '
(44' (
)44( )
;44) *
}55 	
}66 
private88 
static88 -
!CertificatePinningOperationResult88 4
InitializeCore885 C
(88C D
)88D E
{99 
try:: 
{;; 	*
CertificatePinningNativeResult<< *
nativeResult<<+ 7
=<<8 9+
CertificatePinningNativeLibrary<<: Y
.<<Y Z

Initialize<<Z d
(<<d e
)<<e f
;<<f g
if== 
(== 
nativeResult== 
!=== *
CertificatePinningNativeResult==  >
.==> ?
Success==? F
)==F G
{>> 
string?? 
error?? 
=??  
GetErrorStringStatic?? 3
(??3 4
nativeResult??4 @
)??@ A
;??A B
return@@ -
!CertificatePinningOperationResult@@ 8
.@@8 9
	FromError@@9 B
(@@B C%
CertificatePinningFailureAA -
.AA- .)
LIBRARY_INITIALIZATION_FAILEDAA. K
(AAK L
errorAAL Q
)AAQ R
)AAR S
;AAS T
}BB 
returnDD -
!CertificatePinningOperationResultDD 4
.DD4 5
SuccessDD5 <
(DD< =
)DD= >
;DD> ?
}EE 	
catchFF 
(FF 
	ExceptionFF 
exFF 
)FF 
{GG 	
returnHH -
!CertificatePinningOperationResultHH 4
.HH4 5
	FromErrorHH5 >
(HH> ?%
CertificatePinningFailureII )
.II) *$
INITIALIZATION_EXCEPTIONII* B
(IIB C
exIIC E
)IIE F
)IIF G
;IIG H
}JJ 	
}KK 
publicMM 
(
CertificatePinningBoolResultMM '!
VerifyServerSignatureMM( =
(MM= >
ReadOnlyMemoryNN 
<NN 
byteNN 
>NN 
dataNN !
,NN! "
ReadOnlyMemoryOO 
<OO 
byteOO 
>OO 
	signatureOO &
)OO& '
{PP -
!CertificatePinningOperationResultQQ )

stateCheckQQ* 4
=QQ5 6"
ValidateOperationStateQQ7 M
(QQM N
)QQN O
;QQO P
ifRR 

(RR 
!RR 

stateCheckRR 
.RR 
	IsSuccessRR !
)RR! "
{SS 	
returnTT (
CertificatePinningBoolResultTT /
.TT/ 0
	FromErrorTT0 9
(TT9 :

stateCheckTT: D
.TTD E
ERRORTTE J
!TTJ K
)TTK L
;TTL M
}UU 	
ifWW 

(WW 
dataWW 
.WW 
IsEmptyWW 
)WW 
{XX 	
returnYY (
CertificatePinningBoolResultYY /
.YY/ 0
	FromErrorYY0 9
(YY9 :%
CertificatePinningFailureYY: S
.YYS T
MESSAGE_REQUIREDYYT d
(YYd e
)YYe f
)YYf g
;YYg h
}ZZ 	
if\\ 

(\\ 
	signature\\ 
.\\ 
IsEmpty\\ 
)\\ 
{]] 	
return^^ (
CertificatePinningBoolResult^^ /
.^^/ 0
	FromError^^0 9
(^^9 :%
CertificatePinningFailure^^: S
.^^S T"
INVALID_SIGNATURE_SIZE^^T j
(^^j k
$num^^k l
)^^l m
)^^m n
;^^n o
}__ 	
returnaa !
VerifySignatureUnsafeaa $
(aa$ %
dataaa% )
.aa) *
Spanaa* .
,aa. /
	signatureaa0 9
.aa9 :
Spanaa: >
)aa> ?
;aa? @
}bb 
privateee 
staticee (
CertificatePinningBoolResultee /!
VerifySignatureUnsafeee0 E
(eeE F
ReadOnlySpaneeF R
<eeR S
byteeeS W
>eeW X
dataeeY ]
,ee] ^
ReadOnlySpanee_ k
<eek l
byteeel p
>eep q
	signatureeer {
)ee{ |
{ff 
trygg 
{hh 	
unsafeii 
{jj 
fixedkk 
(kk 
bytekk 
*kk 
dataPtrkk $
=kk% &
datakk' +
)kk+ ,
fixedll 
(ll 
bytell 
*ll 
signaturePtrll )
=ll* +
	signaturell, 5
)ll5 6
{mm *
CertificatePinningNativeResultnn 2
resultnn3 9
=nn: ;+
CertificatePinningNativeLibrarynn< [
.nn[ \
VerifySignaturenn\ k
(nnk l
dataPtroo 
,oo  
(oo! "
nuintoo" '
)oo' (
dataoo( ,
.oo, -
Lengthoo- 3
,oo3 4
signaturePtrpp $
,pp$ %
(pp& '
nuintpp' ,
)pp, -
	signaturepp- 6
.pp6 7
Lengthpp7 =
)pp= >
;pp> ?
returnrr 
resultrr !
switchrr" (
{ss *
CertificatePinningNativeResulttt 6
.tt6 7
Successtt7 >
=>tt? A(
CertificatePinningBoolResultttB ^
.tt^ _
	FromValuett_ h
(tth i
truetti m
)ttm n
,ttn o*
CertificatePinningNativeResultuu 6
.uu6 7#
ErrorVerificationFaileduu7 N
=>uuO Q(
CertificatePinningBoolResultuuR n
.uun o
	FromValueuuo x
(uux y
falseuuy ~
)uu~ 
,	uu Ä
_vv 
=>vv (
CertificatePinningBoolResultvv 9
.vv9 :
	FromErrorvv: C
(vvC D%
CertificatePinningFailureww 5
.ww5 6'
ED_25519_VERIFICATION_ERRORww6 Q
(wwQ R 
GetErrorStringStaticwwR f
(wwf g
resultwwg m
)wwm n
)wwn o
)wwo p
}xx 
;xx 
}yy 
}zz 
}{{ 	
catch|| 
(|| 
	Exception|| 
ex|| 
)|| 
{}} 	
return~~ (
CertificatePinningBoolResult~~ /
.~~/ 0
	FromError~~0 9
(~~9 :%
CertificatePinningFailure~~: S
.~~S T+
ED_25519_VERIFICATION_EXCEPTION~~T s
(~~s t
ex~~t v
)~~v w
)~~w x
;~~x y
} 	
}
ÄÄ 
public
ÇÇ 
/
!CertificatePinningByteArrayResult
ÇÇ ,
Encrypt
ÇÇ- 4
(
ÇÇ4 5
ReadOnlyMemory
ÉÉ 
<
ÉÉ 
byte
ÉÉ 
>
ÉÉ 
	plaintext
ÉÉ &
)
ÉÉ& '
{
ÑÑ /
!CertificatePinningOperationResult
ÖÖ )

stateCheck
ÖÖ* 4
=
ÖÖ5 6$
ValidateOperationState
ÖÖ7 M
(
ÖÖM N
)
ÖÖN O
;
ÖÖO P
if
ÜÜ 

(
ÜÜ 
!
ÜÜ 

stateCheck
ÜÜ 
.
ÜÜ 
	IsSuccess
ÜÜ !
)
ÜÜ! "
{
áá 	
return
àà /
!CertificatePinningByteArrayResult
àà 4
.
àà4 5
	FromError
àà5 >
(
àà> ?

stateCheck
àà? I
.
ààI J
ERROR
ààJ O
!
ààO P
)
ààP Q
;
ààQ R
}
ââ 	
if
ãã 

(
ãã 
	plaintext
ãã 
.
ãã 
IsEmpty
ãã 
)
ãã 
{
åå 	
return
çç /
!CertificatePinningByteArrayResult
çç 4
.
çç4 5
	FromError
çç5 >
(
çç> ?'
CertificatePinningFailure
çç? X
.
ççX Y 
PLAINTEXT_REQUIRED
ççY k
(
ççk l
)
ççl m
)
ççm n
;
ççn o
}
éé 	
return
êê 
EncryptUnsafe
êê 
(
êê 
	plaintext
êê &
.
êê& '
Span
êê' +
)
êê+ ,
;
êê, -
}
ëë 
private
îî 
static
îî /
!CertificatePinningByteArrayResult
îî 4
EncryptUnsafe
îî5 B
(
îîB C
ReadOnlySpan
îîC O
<
îîO P
byte
îîP T
>
îîT U
	plaintext
îîV _
)
îî_ `
{
ïï 
try
ññ 
{
óó 	
unsafe
òò 
{
ôô 
fixed
öö 
(
öö 
byte
öö 
*
öö 
plaintextPtr
öö )
=
öö* +
	plaintext
öö, 5
)
öö5 6
{
õõ 
const
úú 
nuint
úú 
MAX_STACK_SIZE
úú  .
=
úú/ 0
$num
úú1 5
;
úú5 6
nuint
ùù 
estimatedSize
ùù '
=
ùù( )
(
ùù* +
nuint
ùù+ 0
)
ùù0 1
	plaintext
ùù1 :
.
ùù: ;
Length
ùù; A
+
ùùB C
$num
ùùD G
;
ùùG H
if
üü 
(
üü 
estimatedSize
üü %
<=
üü& (
MAX_STACK_SIZE
üü) 7
)
üü7 8
{
†† 
byte
°° 
*
°° 
stackBuffer
°° )
=
°°* +

stackalloc
°°, 6
byte
°°7 ;
[
°°; <
(
°°< =
int
°°= @
)
°°@ A
estimatedSize
°°A N
]
°°N O
;
°°O P
nuint
¢¢ 

actualSize
¢¢ (
=
¢¢) *
estimatedSize
¢¢+ 8
;
¢¢8 9,
CertificatePinningNativeResult
§§ 6
result
§§7 =
=
§§> ?-
CertificatePinningNativeLibrary
§§@ _
.
§§_ `
Encrypt
§§` g
(
§§g h
plaintextPtr
•• (
,
••( )
(
••* +
nuint
••+ 0
)
••0 1
	plaintext
••1 :
.
••: ;
Length
••; A
,
••A B
stackBuffer
¶¶ '
,
¶¶' (
&
¶¶) *

actualSize
¶¶* 4
)
¶¶4 5
;
¶¶5 6
if
®® 
(
®® 
result
®® "
==
®®# %,
CertificatePinningNativeResult
®®& D
.
®®D E
Success
®®E L
)
®®L M
{
©© 
byte
™™  
[
™™  !
]
™™! "
output
™™# )
=
™™* +
new
™™, /
byte
™™0 4
[
™™4 5

actualSize
™™5 ?
]
™™? @
;
™™@ A
fixed
´´ !
(
´´" #
byte
´´# '
*
´´' (
	outputPtr
´´) 2
=
´´3 4
output
´´5 ;
)
´´; <
{
¨¨ 
Buffer
≠≠  &
.
≠≠& '

MemoryCopy
≠≠' 1
(
≠≠1 2
stackBuffer
≠≠2 =
,
≠≠= >
	outputPtr
≠≠? H
,
≠≠H I

actualSize
≠≠J T
,
≠≠T U

actualSize
≠≠V `
)
≠≠` a
;
≠≠a b
}
ÆÆ 
return
ØØ "/
!CertificatePinningByteArrayResult
ØØ# D
.
ØØD E
	FromValue
ØØE N
(
ØØN O
output
ØØO U
)
ØØU V
;
ØØV W
}
∞∞ 
return
≤≤ /
!CertificatePinningByteArrayResult
≤≤ @
.
≤≤@ A
	FromError
≤≤A J
(
≤≤J K'
CertificatePinningFailure
≥≥ 5
.
≥≥5 6#
RSA_ENCRYPTION_FAILED
≥≥6 K
(
≥≥K L"
GetErrorStringStatic
≥≥L `
(
≥≥` a
result
≥≥a g
)
≥≥g h
)
≥≥h i
)
≥≥i j
;
≥≥j k
}
¥¥ 
else
µµ 
{
∂∂ 
byte
∑∑ 
[
∑∑ 
]
∑∑ 

ciphertext
∑∑ )
=
∑∑* +
new
∑∑, /
byte
∑∑0 4
[
∑∑4 5
estimatedSize
∑∑5 B
]
∑∑B C
;
∑∑C D
nuint
∏∏ 

actualSize
∏∏ (
=
∏∏) *
estimatedSize
∏∏+ 8
;
∏∏8 9
fixed
∫∫ 
(
∫∫ 
byte
∫∫ #
*
∫∫# $
ciphertextPtr
∫∫% 2
=
∫∫3 4

ciphertext
∫∫5 ?
)
∫∫? @
{
ªª ,
CertificatePinningNativeResult
ºº :
result
ºº; A
=
ººB C-
CertificatePinningNativeLibrary
ººD c
.
ººc d
Encrypt
ººd k
(
ººk l
plaintextPtr
ΩΩ  ,
,
ΩΩ, -
(
ΩΩ. /
nuint
ΩΩ/ 4
)
ΩΩ4 5
	plaintext
ΩΩ5 >
.
ΩΩ> ?
Length
ΩΩ? E
,
ΩΩE F
ciphertextPtr
ææ  -
,
ææ- .
&
ææ/ 0

actualSize
ææ0 :
)
ææ: ;
;
ææ; <
if
¿¿ 
(
¿¿  
result
¿¿  &
==
¿¿' ),
CertificatePinningNativeResult
¿¿* H
.
¿¿H I
Success
¿¿I P
)
¿¿P Q
{
¡¡ 
if
¬¬  "
(
¬¬# $

actualSize
¬¬$ .
!=
¬¬/ 1
estimatedSize
¬¬2 ?
)
¬¬? @
{
√√  !
byte
ƒƒ$ (
[
ƒƒ( )
]
ƒƒ) *
resized
ƒƒ+ 2
=
ƒƒ3 4
new
ƒƒ5 8
byte
ƒƒ9 =
[
ƒƒ= >

actualSize
ƒƒ> H
]
ƒƒH I
;
ƒƒI J
Array
≈≈$ )
.
≈≈) *
Copy
≈≈* .
(
≈≈. /

ciphertext
≈≈/ 9
,
≈≈9 :
resized
≈≈; B
,
≈≈B C
(
≈≈D E
int
≈≈E H
)
≈≈H I

actualSize
≈≈I S
)
≈≈S T
;
≈≈T U
return
∆∆$ */
!CertificatePinningByteArrayResult
∆∆+ L
.
∆∆L M
	FromValue
∆∆M V
(
∆∆V W
resized
∆∆W ^
)
∆∆^ _
;
∆∆_ `
}
««  !
return
»»  &/
!CertificatePinningByteArrayResult
»»' H
.
»»H I
	FromValue
»»I R
(
»»R S

ciphertext
»»S ]
)
»»] ^
;
»»^ _
}
…… 
return
ÀÀ "/
!CertificatePinningByteArrayResult
ÀÀ# D
.
ÀÀD E
	FromError
ÀÀE N
(
ÀÀN O'
CertificatePinningFailure
ÃÃ  9
.
ÃÃ9 :#
RSA_ENCRYPTION_FAILED
ÃÃ: O
(
ÃÃO P"
GetErrorStringStatic
ÃÃP d
(
ÃÃd e
result
ÃÃe k
)
ÃÃk l
)
ÃÃl m
)
ÃÃm n
;
ÃÃn o
}
ÕÕ 
}
ŒŒ 
}
œœ 
}
–– 
}
—— 	
catch
““ 
(
““ 
	Exception
““ 
ex
““ 
)
““ 
{
”” 	
return
‘‘ /
!CertificatePinningByteArrayResult
‘‘ 4
.
‘‘4 5
	FromError
‘‘5 >
(
‘‘> ?'
CertificatePinningFailure
‘‘? X
.
‘‘X Y&
RSA_ENCRYPTION_EXCEPTION
‘‘Y q
(
‘‘q r
ex
‘‘r t
)
‘‘t u
)
‘‘u v
;
‘‘v w
}
’’ 	
}
÷÷ 
public
ÿÿ 
/
!CertificatePinningByteArrayResult
ÿÿ ,
Decrypt
ÿÿ- 4
(
ÿÿ4 5
ReadOnlyMemory
ŸŸ 
<
ŸŸ 
byte
ŸŸ 
>
ŸŸ 

ciphertext
ŸŸ '
)
ŸŸ' (
{
⁄⁄ /
!CertificatePinningOperationResult
€€ )

stateCheck
€€* 4
=
€€5 6$
ValidateOperationState
€€7 M
(
€€M N
)
€€N O
;
€€O P
if
‹‹ 

(
‹‹ 
!
‹‹ 

stateCheck
‹‹ 
.
‹‹ 
	IsSuccess
‹‹ !
)
‹‹! "
{
›› 	
return
ﬁﬁ /
!CertificatePinningByteArrayResult
ﬁﬁ 4
.
ﬁﬁ4 5
	FromError
ﬁﬁ5 >
(
ﬁﬁ> ?

stateCheck
ﬁﬁ? I
.
ﬁﬁI J
ERROR
ﬁﬁJ O
!
ﬁﬁO P
)
ﬁﬁP Q
;
ﬁﬁQ R
}
ﬂﬂ 	
if
·· 

(
·· 

ciphertext
·· 
.
·· 
IsEmpty
·· 
)
·· 
{
‚‚ 	
return
„„ /
!CertificatePinningByteArrayResult
„„ 4
.
„„4 5
	FromError
„„5 >
(
„„> ?'
CertificatePinningFailure
„„? X
.
„„X Y!
CIPHERTEXT_REQUIRED
„„Y l
(
„„l m
)
„„m n
)
„„n o
;
„„o p
}
‰‰ 	
return
ÊÊ 
DecryptUnsafe
ÊÊ 
(
ÊÊ 

ciphertext
ÊÊ '
.
ÊÊ' (
Span
ÊÊ( ,
)
ÊÊ, -
;
ÊÊ- .
}
ÁÁ 
private
ÍÍ 
static
ÍÍ /
!CertificatePinningByteArrayResult
ÍÍ 4
DecryptUnsafe
ÍÍ5 B
(
ÍÍB C
ReadOnlySpan
ÍÍC O
<
ÍÍO P
byte
ÍÍP T
>
ÍÍT U

ciphertext
ÍÍV `
)
ÍÍ` a
{
ÎÎ 
try
ÏÏ 
{
ÌÌ 	
unsafe
ÓÓ 
{
ÔÔ 
fixed
 
(
 
byte
 
*
 
ciphertextPtr
 *
=
+ ,

ciphertext
- 7
)
7 8
{
ÒÒ 
nuint
ÚÚ 
plaintextLen
ÚÚ &
=
ÚÚ' (
(
ÚÚ) *
nuint
ÚÚ* /
)
ÚÚ/ 0

ciphertext
ÚÚ0 :
.
ÚÚ: ;
Length
ÚÚ; A
;
ÚÚA B
byte
ÛÛ 
[
ÛÛ 
]
ÛÛ 
	plaintext
ÛÛ $
=
ÛÛ% &
new
ÛÛ' *
byte
ÛÛ+ /
[
ÛÛ/ 0
plaintextLen
ÛÛ0 <
]
ÛÛ< =
;
ÛÛ= >
fixed
ıı 
(
ıı 
byte
ıı 
*
ıı  
plaintextPtr
ıı! -
=
ıı. /
	plaintext
ıı0 9
)
ıı9 :
{
ˆˆ ,
CertificatePinningNativeResult
˜˜ 6
result
˜˜7 =
=
˜˜> ?-
CertificatePinningNativeLibrary
˜˜@ _
.
˜˜_ `
Decrypt
˜˜` g
(
˜˜g h
ciphertextPtr
¯¯ )
,
¯¯) *
(
¯¯+ ,
nuint
¯¯, 1
)
¯¯1 2

ciphertext
¯¯2 <
.
¯¯< =
Length
¯¯= C
,
¯¯C D
plaintextPtr
˘˘ (
,
˘˘( )
&
˘˘* +
plaintextLen
˘˘+ 7
)
˘˘7 8
;
˘˘8 9
if
˚˚ 
(
˚˚ 
result
˚˚ "
==
˚˚# %,
CertificatePinningNativeResult
˚˚& D
.
˚˚D E
Success
˚˚E L
)
˚˚L M
{
¸¸ 
if
˝˝ 
(
˝˝  
plaintextLen
˝˝  ,
!=
˝˝- /
(
˝˝0 1
nuint
˝˝1 6
)
˝˝6 7

ciphertext
˝˝7 A
.
˝˝A B
Length
˝˝B H
)
˝˝H I
{
˛˛ 
byte
ˇˇ  $
[
ˇˇ$ %
]
ˇˇ% &
resized
ˇˇ' .
=
ˇˇ/ 0
new
ˇˇ1 4
byte
ˇˇ5 9
[
ˇˇ9 :
plaintextLen
ˇˇ: F
]
ˇˇF G
;
ˇˇG H
Array
ÄÄ  %
.
ÄÄ% &
Copy
ÄÄ& *
(
ÄÄ* +
	plaintext
ÄÄ+ 4
,
ÄÄ4 5
resized
ÄÄ6 =
,
ÄÄ= >
(
ÄÄ? @
int
ÄÄ@ C
)
ÄÄC D
plaintextLen
ÄÄD P
)
ÄÄP Q
;
ÄÄQ R
return
ÅÅ  &/
!CertificatePinningByteArrayResult
ÅÅ' H
.
ÅÅH I
	FromValue
ÅÅI R
(
ÅÅR S
resized
ÅÅS Z
)
ÅÅZ [
;
ÅÅ[ \
}
ÇÇ 
return
ÉÉ "/
!CertificatePinningByteArrayResult
ÉÉ# D
.
ÉÉD E
	FromValue
ÉÉE N
(
ÉÉN O
	plaintext
ÉÉO X
)
ÉÉX Y
;
ÉÉY Z
}
ÑÑ 
return
ÜÜ /
!CertificatePinningByteArrayResult
ÜÜ @
.
ÜÜ@ A
	FromError
ÜÜA J
(
ÜÜJ K'
CertificatePinningFailure
áá 5
.
áá5 6#
RSA_DECRYPTION_FAILED
áá6 K
(
ááK L"
GetErrorStringStatic
ááL `
(
áá` a
result
ááa g
)
áág h
)
ááh i
)
áái j
;
ááj k
}
àà 
}
ââ 
}
ää 
}
ãã 	
catch
åå 
(
åå 
	Exception
åå 
ex
åå 
)
åå 
{
çç 	
return
éé /
!CertificatePinningByteArrayResult
éé 4
.
éé4 5
	FromError
éé5 >
(
éé> ?'
CertificatePinningFailure
éé? X
.
ééX Y&
RSA_DECRYPTION_EXCEPTION
ééY q
(
ééq r
ex
éér t
)
éét u
)
ééu v
;
éév w
}
èè 	
}
êê 
public
íí 
/
!CertificatePinningByteArrayResult
íí ,
GetPublicKey
íí- 9
(
íí9 :
)
íí: ;
{
ìì /
!CertificatePinningOperationResult
îî )

stateCheck
îî* 4
=
îî5 6$
ValidateOperationState
îî7 M
(
îîM N
)
îîN O
;
îîO P
if
ïï 

(
ïï 
!
ïï 

stateCheck
ïï 
.
ïï 
	IsSuccess
ïï !
)
ïï! "
{
ññ 	
return
óó /
!CertificatePinningByteArrayResult
óó 4
.
óó4 5
	FromError
óó5 >
(
óó> ?

stateCheck
óó? I
.
óóI J
ERROR
óóJ O
!
óóO P
)
óóP Q
;
óóQ R
}
òò 	
return
öö  
GetPublicKeyUnsafe
öö !
(
öö! "
)
öö" #
;
öö# $
}
õõ 
private
ûû 
static
ûû /
!CertificatePinningByteArrayResult
ûû 4 
GetPublicKeyUnsafe
ûû5 G
(
ûûG H
)
ûûH I
{
üü 
try
†† 
{
°° 	
unsafe
¢¢ 
{
££ 
const
§§ 
nuint
§§ %
INITIAL_KEY_BUFFER_SIZE
§§ 3
=
§§4 5
$num
§§6 :
;
§§: ;
nuint
•• 
keyLen
•• 
=
•• %
INITIAL_KEY_BUFFER_SIZE
•• 6
;
••6 7
byte
¶¶ 
[
¶¶ 
]
¶¶ 
	publicKey
¶¶  
=
¶¶! "
new
¶¶# &
byte
¶¶' +
[
¶¶+ ,
keyLen
¶¶, 2
]
¶¶2 3
;
¶¶3 4
fixed
®® 
(
®® 
byte
®® 
*
®® 
keyPtr
®® #
=
®®$ %
	publicKey
®®& /
)
®®/ 0
{
©© ,
CertificatePinningNativeResult
™™ 2
result
™™3 9
=
™™: ;-
CertificatePinningNativeLibrary
™™< [
.
™™[ \
GetPublicKey
™™\ h
(
™™h i
keyPtr
™™i o
,
™™o p
&
™™q r
keyLen
™™r x
)
™™x y
;
™™y z
if
¨¨ 
(
¨¨ 
result
¨¨ 
==
¨¨ !,
CertificatePinningNativeResult
¨¨" @
.
¨¨@ A
Success
¨¨A H
)
¨¨H I
{
≠≠ 
if
ÆÆ 
(
ÆÆ 
keyLen
ÆÆ "
!=
ÆÆ# %%
INITIAL_KEY_BUFFER_SIZE
ÆÆ& =
)
ÆÆ= >
{
ØØ 
byte
∞∞  
[
∞∞  !
]
∞∞! "
resized
∞∞# *
=
∞∞+ ,
new
∞∞- 0
byte
∞∞1 5
[
∞∞5 6
keyLen
∞∞6 <
]
∞∞< =
;
∞∞= >
Array
±± !
.
±±! "
Copy
±±" &
(
±±& '
	publicKey
±±' 0
,
±±0 1
resized
±±2 9
,
±±9 :
(
±±; <
int
±±< ?
)
±±? @
keyLen
±±@ F
)
±±F G
;
±±G H
return
≤≤ "/
!CertificatePinningByteArrayResult
≤≤# D
.
≤≤D E
	FromValue
≤≤E N
(
≤≤N O
resized
≤≤O V
)
≤≤V W
;
≤≤W X
}
≥≥ 
return
¥¥ /
!CertificatePinningByteArrayResult
¥¥ @
.
¥¥@ A
	FromValue
¥¥A J
(
¥¥J K
	publicKey
¥¥K T
)
¥¥T U
;
¥¥U V
}
µµ 
return
∑∑ /
!CertificatePinningByteArrayResult
∑∑ <
.
∑∑< =
	FromError
∑∑= F
(
∑∑F G'
CertificatePinningFailure
∏∏ 1
.
∏∏1 2+
CERTIFICATE_VALIDATION_FAILED
∏∏2 O
(
∏∏O P"
GetErrorStringStatic
∏∏P d
(
∏∏d e
result
∏∏e k
)
∏∏k l
)
∏∏l m
)
∏∏m n
;
∏∏n o
}
ππ 
}
∫∫ 
}
ªª 	
catch
ºº 
(
ºº 
	Exception
ºº 
ex
ºº 
)
ºº 
{
ΩΩ 	
return
ææ /
!CertificatePinningByteArrayResult
ææ 4
.
ææ4 5
	FromError
ææ5 >
(
ææ> ?'
CertificatePinningFailure
ææ? X
.
ææX Y.
 CERTIFICATE_VALIDATION_EXCEPTION
ææY y
(
ææy z
ex
ææz |
)
ææ| }
)
ææ} ~
;
ææ~ 
}
øø 	
}
¿¿ 
private
¬¬ /
!CertificatePinningOperationResult
¬¬ -$
ValidateOperationState
¬¬. D
(
¬¬D E
)
¬¬E F
{
√√ 
return
ƒƒ 
_state
ƒƒ 
switch
ƒƒ 
{
≈≈ 	
DISPOSED
∆∆ 
=>
∆∆ /
!CertificatePinningOperationResult
∆∆ 9
.
∆∆9 :
	FromError
∆∆: C
(
∆∆C D'
CertificatePinningFailure
∆∆D ]
.
∆∆] ^
SERVICE_DISPOSED
∆∆^ n
(
∆∆n o
)
∆∆o p
)
∆∆p q
,
∆∆q r
NOT_INITIALIZED
«« 
=>
«« /
!CertificatePinningOperationResult
«« @
.
««@ A
	FromError
««A J
(
««J K'
CertificatePinningFailure
««K d
.
««d e%
SERVICE_NOT_INITIALIZED
««e |
(
««| }
)
««} ~
)
««~ 
,«« Ä
INITIALIZING
»» 
=>
»» /
!CertificatePinningOperationResult
»» =
.
»»= >
	FromError
»»> G
(
»»G H'
CertificatePinningFailure
»»H a
.
»»a b"
SERVICE_INITIALIZING
»»b v
(
»»v w
)
»»w x
)
»»x y
,
»»y z
INITIALIZED
…… 
=>
…… /
!CertificatePinningOperationResult
…… <
.
……< =
Success
……= D
(
……D E
)
……E F
,
……F G
_
   
=>
   /
!CertificatePinningOperationResult
   2
.
  2 3
	FromError
  3 <
(
  < ='
CertificatePinningFailure
  = V
.
  V W#
SERVICE_INVALID_STATE
  W l
(
  l m
)
  m n
)
  n o
}
ÀÀ 	
;
ÀÀ	 

}
ÃÃ 
private
ŒŒ 
static
ŒŒ 
unsafe
ŒŒ 
string
ŒŒ  "
GetErrorStringStatic
ŒŒ! 5
(
ŒŒ5 6,
CertificatePinningNativeResult
ŒŒ6 T
result
ŒŒU [
)
ŒŒ[ \
{
œœ 
try
–– 
{
—— 	
byte
““ 
*
““ 
errorPtr
““ 
=
““ -
CertificatePinningNativeLibrary
““ <
.
““< =
GetErrorMessage
““= L
(
““L M
)
““M N
;
““N O
if
”” 
(
”” 
errorPtr
”” 
!=
”” 
null
””  
)
””  !
{
‘‘ 
return
’’ 
Marshal
’’ 
.
’’ 
PtrToStringUTF8
’’ .
(
’’. /
(
’’/ 0
IntPtr
’’0 6
)
’’6 7
errorPtr
’’7 ?
)
’’? @
??
’’A C
string
’’D J
.
’’J K
Create
’’K Q
(
’’Q R
CultureInfo
’’R ]
.
’’] ^
InvariantCulture
’’^ n
,
’’n o
$"
’’p r
$str’’r Å
{’’Å Ç
result’’Ç à
}’’à â
"’’â ä
)’’ä ã
;’’ã å
}
÷÷ 
}
◊◊ 	
catch
ÿÿ 
(
ÿÿ 
	Exception
ÿÿ 
ex
ÿÿ 
)
ÿÿ 
{
ŸŸ 	
Serilog
⁄⁄ 
.
⁄⁄ 
Log
⁄⁄ 
.
⁄⁄ 
Warning
⁄⁄ 
(
⁄⁄  
ex
⁄⁄  "
,
⁄⁄" #
$str
⁄⁄$ u
,
⁄⁄u v
result
€€ 
)
€€ 
;
€€ 
}
‹‹ 	
return
ﬁﬁ 
string
ﬁﬁ 
.
ﬁﬁ 
Create
ﬁﬁ 
(
ﬁﬁ 
CultureInfo
ﬁﬁ (
.
ﬁﬁ( )
InvariantCulture
ﬁﬁ) 9
,
ﬁﬁ9 :
$"
ﬁﬁ; =
$str
ﬁﬁ= I
{
ﬁﬁI J
result
ﬁﬁJ P
}
ﬁﬁP Q
"
ﬁﬁQ R
)
ﬁﬁR S
;
ﬁﬁS T
}
ﬂﬂ 
public
·· 

async
·· 
	ValueTask
·· 
DisposeAsync
·· '
(
··' (
)
··( )
{
‚‚ 
if
„„ 

(
„„ 
Interlocked
„„ 
.
„„ 
Exchange
„„  
(
„„  !
ref
„„! $
_state
„„% +
,
„„+ ,
DISPOSED
„„- 5
)
„„5 6
==
„„7 9
DISPOSED
„„: B
)
„„B C
{
‰‰ 	
return
ÂÂ 
;
ÂÂ 
}
ÊÊ 	
try
ËË 
{
ÈÈ 	
await
ÍÍ 
Task
ÍÍ 
.
ÍÍ 
Run
ÍÍ 
(
ÍÍ 
static
ÍÍ !
(
ÍÍ" #
)
ÍÍ# $
=>
ÍÍ% '
{
ÎÎ 
try
ÏÏ 
{
ÌÌ -
CertificatePinningNativeLibrary
ÓÓ 3
.
ÓÓ3 4
Cleanup
ÓÓ4 ;
(
ÓÓ; <
)
ÓÓ< =
;
ÓÓ= >
}
ÔÔ 
catch
 
(
 
	Exception
  
ex
! #
)
# $
{
ÒÒ 
Serilog
ÚÚ 
.
ÚÚ 
Log
ÚÚ 
.
ÚÚ  
Warning
ÚÚ  '
(
ÚÚ' (
ex
ÚÚ( *
,
ÚÚ* +
$str
ÚÚ, i
)
ÚÚi j
;
ÚÚj k
}
ÛÛ 
}
ÙÙ 
)
ÙÙ 
.
ÙÙ 
ConfigureAwait
ÙÙ 
(
ÙÙ 
false
ÙÙ #
)
ÙÙ# $
;
ÙÙ$ %
}
ıı 	
finally
ˆˆ 
{
˜˜ 	!
_initializationLock
¯¯ 
.
¯¯  
Dispose
¯¯  '
(
¯¯' (
)
¯¯( )
;
¯¯) *
}
˘˘ 	
}
˙˙ 
}˚˚ ·
é/Users/oleksandrmelnychenko/RiderProjects/ecliptix-desktop/Ecliptix.Security.Certificate.Pinning/Services/CertificatePinningOperationResult.cs
	namespace 	
Ecliptix
 
. 
Security 
. 
Certificate '
.' (
Pinning( /
./ 0
Services0 8
;8 9
public 
readonly 
struct -
!CertificatePinningOperationResult 8
{ 
private -
!CertificatePinningOperationResult -
(- .
bool. 2
	isSuccess3 <
,< =%
CertificatePinningFailure> W
?W X
errorY ^
)^ _
{ 
	IsSuccess		 
=		 
	isSuccess		 
;		 
ERROR

 
=

 
error

 
;

 
} 
public 

bool 
	IsSuccess 
{ 
get 
;  
}! "
public 
%
CertificatePinningFailure $
?$ %
ERROR& +
{, -
get. 1
;1 2
}3 4
public 

static -
!CertificatePinningOperationResult 3
Success4 ;
(; <
)< =
=>> @
newA D
(D E
trueE I
,I J
nullK O
)O P
;P Q
public 

static -
!CertificatePinningOperationResult 3
	FromError4 =
(= >%
CertificatePinningFailure> W
errorX ]
)] ^
=>_ a
newb e
(e f
falsef k
,k l
errorm r
)r s
;s t
} ≤
é/Users/oleksandrmelnychenko/RiderProjects/ecliptix-desktop/Ecliptix.Security.Certificate.Pinning/Services/CertificatePinningByteArrayResult.cs
	namespace 	
Ecliptix
 
. 
Security 
. 
Certificate '
.' (
Pinning( /
./ 0
Services0 8
;8 9
public 
readonly 
struct -
!CertificatePinningByteArrayResult 8
{ 
private -
!CertificatePinningByteArrayResult -
(- .
bool. 2
	isSuccess3 <
,< =
byte> B
[B C
]C D
?D E
valueF K
,K L%
CertificatePinningFailureM f
?f g
errorh m
)m n
{ 
	IsSuccess		 
=		 
	isSuccess		 
;		 
Value

 
=

 
value

 
;

 
ERROR 
= 
error 
; 
} 
public 

bool 
	IsSuccess 
{ 
get 
;  
}! "
public 

byte 
[ 
] 
? 
Value 
{ 
get 
; 
}  !
public 
%
CertificatePinningFailure $
?$ %
ERROR& +
{, -
get. 1
;1 2
}3 4
public 

static -
!CertificatePinningByteArrayResult 3
	FromValue4 =
(= >
byte> B
[B C
]C D
valueE J
)J K
=>L N
newO R
(R S
trueS W
,W X
valueY ^
,^ _
null` d
)d e
;e f
public 

static -
!CertificatePinningByteArrayResult 3
	FromError4 =
(= >%
CertificatePinningFailure> W
errorX ]
)] ^
=>_ a
newb e
(e f
falsef k
,k l
nullm q
,q r
errors x
)x y
;y z
} ¢
â/Users/oleksandrmelnychenko/RiderProjects/ecliptix-desktop/Ecliptix.Security.Certificate.Pinning/Services/CertificatePinningBoolResult.cs
	namespace 	
Ecliptix
 
. 
Security 
. 
Certificate '
.' (
Pinning( /
./ 0
Services0 8
;8 9
public 
readonly 
struct (
CertificatePinningBoolResult 3
{ 
private (
CertificatePinningBoolResult (
(( )
bool) -
	isSuccess. 7
,7 8
bool9 =
value> C
,C D%
CertificatePinningFailureE ^
?^ _
error` e
)e f
{ 
	IsSuccess		 
=		 
	isSuccess		 
;		 
Value

 
=

 
value

 
;

 
ERROR 
= 
error 
; 
} 
public 

bool 
	IsSuccess 
{ 
get 
;  
}! "
public 

bool 
Value 
{ 
get 
; 
} 
public 
%
CertificatePinningFailure $
?$ %
ERROR& +
{, -
get. 1
;1 2
}3 4
public 

static (
CertificatePinningBoolResult .
	FromValue/ 8
(8 9
bool9 =
value> C
)C D
=>E G
newH K
(K L
trueL P
,P Q
valueR W
,W X
nullY ]
)] ^
;^ _
public 

static (
CertificatePinningBoolResult .
	FromError/ 8
(8 9%
CertificatePinningFailure9 R
errorS X
)X Y
=>Z \
new] `
(` a
falsea f
,f g
falseh m
,m n
erroro t
)t u
;u v
} ¯
â/Users/oleksandrmelnychenko/RiderProjects/ecliptix-desktop/Ecliptix.Security.Certificate.Pinning/Native/CertificatePinningNativeResult.cs
	namespace 	
Ecliptix
 
. 
Security 
. 
Certificate '
.' (
Pinning( /
./ 0
Native0 6
;6 7
public 
enum *
CertificatePinningNativeResult *
{ 
Success 
= 
$num 
, 
ErrorInvalidParams 
= 
- 
$num 
, 
ErrorCryptoFailure 
= 
- 
$num 
, #
ErrorVerificationFailed 
= 
- 
$num  
,  !
ErrorInitFailed		 
=		 
-		 
$num		 
}

 ôS
ä/Users/oleksandrmelnychenko/RiderProjects/ecliptix-desktop/Ecliptix.Security.Certificate.Pinning/Native/CertificatePinningNativeLibrary.cs
	namespace 	
Ecliptix
 
. 
Security 
. 
Certificate '
.' (
Pinning( /
./ 0
Native0 6
;6 7
internal 
static	 
unsafe 
class +
CertificatePinningNativeLibrary <
{		 
private

 
const

 
string

 
LIBRARY_NAME

 %
=

& ''
CertificatePinningConstants

( C
.

C D
LibraryNames

D P
.

P Q
SSL_PINNING

Q \
;

\ ]
static 
+
CertificatePinningNativeLibrary *
(* +
)+ ,
{ 
NativeLibrary 
.  
SetDllImportResolver *
(* +
Assembly+ 3
.3 4 
GetExecutingAssembly4 H
(H I
)I J
,J K
ImportResolverL Z
)Z [
;[ \
} 
[ (
UnconditionalSuppressMessage !
(! "
$str" .
,. /
$str0 |
,| }
Justification	~ ã
=
å ç
$str
é ¬
)
¬ √
]
√ ƒ
[ (
UnconditionalSuppressMessage !
(! "
$str" ,
,, -
$str	. π
,
π ∫
Justification
ª »
=
…  
$str
À ˆ
)
ˆ ˜
]
˜ ¯
private 
static 
IntPtr 
ImportResolver (
(( )
string) /
libraryName0 ;
,; <
Assembly= E
assemblyF N
,N O
DllImportSearchPathP c
?c d

searchPathe o
)o p
{ 
if 

( 
libraryName 
!= 
LIBRARY_NAME '
)' (
{ 	
return 
IntPtr 
. 
Zero 
; 
} 	
string 
	extension 
; 
string 
fileName 
; 
if 

( 
RuntimeInformation 
. 
IsOSPlatform +
(+ ,

OSPlatform, 6
.6 7
Windows7 >
)> ?
)? @
{ 	
fileName 
= 
$str ,
;, -
}   	
else!! 
if!! 
(!! 
RuntimeInformation!! #
.!!# $
IsOSPlatform!!$ 0
(!!0 1

OSPlatform!!1 ;
.!!; <
OSX!!< ?
)!!? @
)!!@ A
{"" 	
	extension## 
=## 
$str##  
;##  !
fileName$$ 
=$$ 
$"$$ 
{$$ 
LIBRARY_NAME$$ &
}$$& '
{$$' (
	extension$$( 1
}$$1 2
"$$2 3
;$$3 4
}%% 	
else&& 
{'' 	
	extension(( 
=(( 
$str(( 
;(( 
fileName)) 
=)) 
$")) 
{)) 
LIBRARY_NAME)) &
}))& '
{))' (
	extension))( 1
}))1 2
"))2 3
;))3 4
}** 	
string,, 
[,, 
],, 
searchPaths,, 
=,, 
[-- 	
Path.. 
... 
Combine.. 
(.. 

AppContext.. #
...# $
BaseDirectory..$ 1
,..1 2
fileName..3 ;
)..; <
,..< =
Path// 
.// 
Combine// 
(// 

AppContext// #
.//# $
BaseDirectory//$ 1
,//1 2
$str//3 =
,//= > 
GetRuntimeIdentifier//? S
(//S T
)//T U
,//U V
$str//W _
,//_ `
fileName//a i
)//i j
,//j k
Path00 
.00 
Combine00 
(00 
Path00 
.00 
GetDirectoryName00 .
(00. /
assembly00/ 7
.007 8
Location008 @
)00@ A
??00B D
string00E K
.00K L
Empty00L Q
,00Q R
fileName00S [
)00[ \
]11 	
;11	 

foreach33 
(33 
string33 
libPath33 
in33  "
searchPaths33# .
)33. /
{44 	
if55 
(55 
!55 
File55 
.55 
Exists55 
(55 
libPath55 $
)55$ %
)55% &
{66 
continue77 
;77 
}88 
try:: 
{;; 
return<< 
NativeLibrary<< $
.<<$ %
Load<<% )
(<<) *
libPath<<* 1
)<<1 2
;<<2 3
}== 
catch>> 
(>> 
	Exception>> 
)>> 
{?? 
}AA 
}BB 	
returnDD 
IntPtrDD 
.DD 
ZeroDD 
;DD 
}EE 
privateGG 
staticGG 
stringGG  
GetRuntimeIdentifierGG .
(GG. /
)GG/ 0
{HH 
ifII 

(II 
RuntimeInformationII 
.II 
IsOSPlatformII +
(II+ ,

OSPlatformII, 6
.II6 7
WindowsII7 >
)II> ?
)II? @
{JJ 	
returnKK 
RuntimeInformationKK %
.KK% &
ProcessArchitectureKK& 9
==KK: <
ArchitectureKK= I
.KKI J
X86KKJ M
?KKN O
$strKKP Y
:KKZ [
$strKK\ e
;KKe f
}LL 	
ifMM 

(MM 
RuntimeInformationMM 
.MM 
IsOSPlatformMM +
(MM+ ,

OSPlatformMM, 6
.MM6 7
OSXMM7 :
)MM: ;
)MM; <
{NN 	
returnOO 
RuntimeInformationOO %
.OO% &
ProcessArchitectureOO& 9
==OO: <
ArchitectureOO= I
.OOI J
Arm64OOJ O
?OOP Q
$strOOR ]
:OO^ _
$strOO` i
;OOi j
}PP 	
returnQQ 
$strQQ 
;QQ 
}RR 
[TT 
	DllImportTT 
(TT 
LIBRARY_NAMETT 
,TT 

EntryPointTT '
=TT( )
$strTT* @
,TT@ A
CallingConventionTTB S
=TTT U
CallingConventionTTV g
.TTg h
CdeclTTh m
)TTm n
]TTn o
publicUU 

staticUU 
externUU *
CertificatePinningNativeResultUU 7

InitializeUU8 B
(UUB C
)UUC D
;UUD E
[WW 
	DllImportWW 
(WW 
LIBRARY_NAMEWW 
,WW 

EntryPointWW '
=WW( )
$strWW* C
,WWC D
CallingConventionWWE V
=WWW X
CallingConventionWWY j
.WWj k
CdeclWWk p
)WWp q
]WWq r
publicXX 

staticXX 
externXX 
voidXX 
CleanupXX %
(XX% &
)XX& '
;XX' (
[ZZ 
	DllImportZZ 
(ZZ 
LIBRARY_NAMEZZ 
,ZZ 

EntryPointZZ '
=ZZ( )
$strZZ* B
,ZZB C
CallingConventionZZD U
=ZZV W
CallingConventionZZX i
.ZZi j
CdeclZZj o
)ZZo p
]ZZp q
public[[ 

static[[ 
extern[[ *
CertificatePinningNativeResult[[ 7
VerifySignature[[8 G
([[G H
byte\\ 
*\\ 
data\\ 
,\\ 
nuint\\ 
dataLen\\ !
,\\! "
byte]] 
*]] 
	signature]] 
,]] 
nuint]] 
signatureLen]] +
)]]+ ,
;]], -
[__ 
	DllImport__ 
(__ 
LIBRARY_NAME__ 
,__ 

EntryPoint__ '
=__( )
$str__* C
,__C D
CallingConvention__E V
=__W X
CallingConvention__Y j
.__j k
Cdecl__k p
)__p q
]__q r
public`` 

static`` 
extern`` *
CertificatePinningNativeResult`` 7
Encrypt``8 ?
(``? @
byteaa 
*aa 
	plaintextaa 
,aa 
nuintaa 
plaintextLenaa +
,aa+ ,
bytebb 
*bb 

ciphertextbb 
,bb 
nuintbb 
*bb  
ciphertextLenbb! .
)bb. /
;bb/ 0
[dd 
	DllImportdd 
(dd 
LIBRARY_NAMEdd 
,dd 

EntryPointdd '
=dd( )
$strdd* C
,ddC D
CallingConventionddE V
=ddW X
CallingConventionddY j
.ddj k
Cdeclddk p
)ddp q
]ddq r
publicee 

staticee 
externee *
CertificatePinningNativeResultee 7
Decryptee8 ?
(ee? @
byteff 
*ff 

ciphertextff 
,ff 
nuintff 
ciphertextLenff  -
,ff- .
bytegg 
*gg 
	plaintextgg 
,gg 
nuintgg 
*gg 
plaintextLengg  ,
)gg, -
;gg- .
[ii 
	DllImportii 
(ii 
LIBRARY_NAMEii 
,ii 

EntryPointii '
=ii( )
$strii* J
,iiJ K
CallingConventioniiL ]
=ii^ _
CallingConventionii` q
.iiq r
Cdecliir w
)iiw x
]iix y
publicjj 

staticjj 
externjj *
CertificatePinningNativeResultjj 7
GetPublicKeyjj8 D
(jjD E
bytekk 
*kk 
publicKeyDerkk 
,kk 
nuintkk !
*kk! "
publicKeyLenkk# /
)kk/ 0
;kk0 1
[mm 
	DllImportmm 
(mm 
LIBRARY_NAMEmm 
,mm 

EntryPointmm '
=mm( )
$strmm* E
,mmE F
CallingConventionmmG X
=mmY Z
CallingConventionmm[ l
.mml m
Cdeclmmm r
)mmr s
]mms t
publicnn 

staticnn 
externnn 
bytenn 
*nn 
GetErrorMessagenn .
(nn. /
)nn/ 0
;nn0 1
}oo ÷
â/Users/oleksandrmelnychenko/RiderProjects/ecliptix-desktop/Ecliptix.Security.Certificate.Pinning/Constants/CertificatePinningConstants.cs
	namespace 	
Ecliptix
 
. 
Security 
. 
Certificate '
.' (
Pinning( /
./ 0
	Constants0 9
;9 :
internal 
static	 
class '
CertificatePinningConstants 1
{ 
internal 
static 
class 
LibraryNames &
{ 
internal 
const 
string 
SSL_PINNING )
=* +
$str, ;
;; <
} 
}		 