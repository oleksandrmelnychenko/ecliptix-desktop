ﬂ
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
} ‰4
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
}gg ÉÇ
Ü/Users/oleksandrmelnychenko/RiderProjects/ecliptix-desktop/Ecliptix.Security.Certificate.Pinning/Services/CertificatePinningService.cs
	namespace 	
Ecliptix
 
. 
Security 
. 
Certificate '
.' (
Pinning( /
./ 0
Services0 8
;8 9
public 
sealed 
class %
CertificatePinningService -
:. /
IAsyncDisposable0 @
{ 
private		 
const		 
int		 
NOT_INITIALIZED		 %
=		& '
$num		( )
;		) *
private

 
const

 
int

 
INITIALIZING

 "
=

# $
$num

% &
;

& '
private 
const 
int 
INITIALIZED !
=" #
$num$ %
;% &
private 
const 
int 
DISPOSED 
=  
$num! "
;" #
private 
volatile 
int 
_state 
=  !
NOT_INITIALIZED" 1
;1 2
private 
readonly 
SemaphoreSlim "
_initializationLock# 6
=7 8
new9 <
(< =
$num= >
,> ?
$num@ A
)A B
;B C
public 
-
!CertificatePinningOperationResult ,

Initialize- 7
(7 8
CancellationToken8 I
cancellationTokenJ [
=\ ]
default^ e
)e f
{ 
if 

( 
_state 
== 
DISPOSED 
) 
{ 	
return -
!CertificatePinningOperationResult 4
.4 5
	FromError5 >
(> ?%
CertificatePinningFailure? X
.X Y
SERVICE_DISPOSEDY i
(i j
)j k
)k l
;l m
} 	
if 

( 
_state 
== 
INITIALIZED !
)! "
{ 	
return -
!CertificatePinningOperationResult 4
.4 5
Success5 <
(< =
)= >
;> ?
} 	
_initializationLock 
. 
Wait  
(  !
cancellationToken! 2
)2 3
;3 4
try 
{ 	
if   
(   
_state   
==   
INITIALIZED   %
)  % &
{!! 
return"" -
!CertificatePinningOperationResult"" 8
.""8 9
Success""9 @
(""@ A
)""A B
;""B C
}## 
if%% 
(%% 
_state%% 
==%% 
DISPOSED%% "
)%%" #
{&& 
return'' -
!CertificatePinningOperationResult'' 8
.''8 9
	FromError''9 B
(''B C%
CertificatePinningFailure''C \
.''\ ]
SERVICE_DISPOSED''] m
(''m n
)''n o
)''o p
;''p q
}(( 
Interlocked** 
.** 
Exchange**  
(**  !
ref**! $
_state**% +
,**+ ,
INITIALIZING**- 9
)**9 :
;**: ;-
!CertificatePinningOperationResult,, -
result,,. 4
=,,5 6
InitializeCore,,7 E
(,,E F
),,F G
;,,G H
Interlocked.. 
... 
Exchange..  
(..  !
ref..! $
_state..% +
,..+ ,
result..- 3
...3 4
	IsSuccess..4 =
?..> ?
INITIALIZED..@ K
:..L M
NOT_INITIALIZED..N ]
)..] ^
;..^ _
return// 
result// 
;// 
}00 	
finally11 
{22 	
_initializationLock33 
.33  
Release33  '
(33' (
)33( )
;33) *
}44 	
}55 
private77 
static77 -
!CertificatePinningOperationResult77 4
InitializeCore775 C
(77C D
)77D E
{88 
try99 
{:: 	*
CertificatePinningNativeResult;; *
nativeResult;;+ 7
=;;8 9+
CertificatePinningNativeLibrary;;: Y
.;;Y Z

Initialize;;Z d
(;;d e
);;e f
;;;f g
if<< 
(<< 
nativeResult<< 
!=<< *
CertificatePinningNativeResult<<  >
.<<> ?
Success<<? F
)<<F G
{== 
string>> 
error>> 
=>>  
GetErrorStringStatic>> 3
(>>3 4
nativeResult>>4 @
)>>@ A
;>>A B
return?? -
!CertificatePinningOperationResult?? 8
.??8 9
	FromError??9 B
(??B C%
CertificatePinningFailure@@ -
.@@- .)
LIBRARY_INITIALIZATION_FAILED@@. K
(@@K L
error@@L Q
)@@Q R
)@@R S
;@@S T
}AA 
returnCC -
!CertificatePinningOperationResultCC 4
.CC4 5
SuccessCC5 <
(CC< =
)CC= >
;CC> ?
}DD 	
catchEE 
(EE 
	ExceptionEE 
exEE 
)EE 
{FF 	
returnGG -
!CertificatePinningOperationResultGG 4
.GG4 5
	FromErrorGG5 >
(GG> ?%
CertificatePinningFailureHH )
.HH) *$
INITIALIZATION_EXCEPTIONHH* B
(HHB C
exHHC E
)HHE F
)HHF G
;HHG H
}II 	
}JJ 
publicLL 
(
CertificatePinningBoolResultLL '!
VerifyServerSignatureLL( =
(LL= >
ReadOnlyMemoryMM 
<MM 
byteMM 
>MM 
dataMM !
,MM! "
ReadOnlyMemoryNN 
<NN 
byteNN 
>NN 
	signatureNN &
)NN& '
{OO -
!CertificatePinningOperationResultPP )

stateCheckPP* 4
=PP5 6"
ValidateOperationStatePP7 M
(PPM N
)PPN O
;PPO P
ifQQ 

(QQ 
!QQ 

stateCheckQQ 
.QQ 
	IsSuccessQQ !
)QQ! "
{RR 	
returnSS (
CertificatePinningBoolResultSS /
.SS/ 0
	FromErrorSS0 9
(SS9 :

stateCheckSS: D
.SSD E
ERRORSSE J
!SSJ K
)SSK L
;SSL M
}TT 	
ifVV 

(VV 
dataVV 
.VV 
IsEmptyVV 
)VV 
{WW 	
returnXX (
CertificatePinningBoolResultXX /
.XX/ 0
	FromErrorXX0 9
(XX9 :%
CertificatePinningFailureXX: S
.XXS T
MESSAGE_REQUIREDXXT d
(XXd e
)XXe f
)XXf g
;XXg h
}YY 	
if[[ 

([[ 
	signature[[ 
.[[ 
IsEmpty[[ 
)[[ 
{\\ 	
return]] (
CertificatePinningBoolResult]] /
.]]/ 0
	FromError]]0 9
(]]9 :%
CertificatePinningFailure]]: S
.]]S T"
INVALID_SIGNATURE_SIZE]]T j
(]]j k
$num]]k l
)]]l m
)]]m n
;]]n o
}^^ 	
return`` !
VerifySignatureUnsafe`` $
(``$ %
data``% )
.``) *
Span``* .
,``. /
	signature``0 9
.``9 :
Span``: >
)``> ?
;``? @
}aa 
privatedd 
staticdd (
CertificatePinningBoolResultdd /!
VerifySignatureUnsafedd0 E
(ddE F
ReadOnlySpanddF R
<ddR S
byteddS W
>ddW X
dataddY ]
,dd] ^
ReadOnlySpandd_ k
<ddk l
byteddl p
>ddp q
	signatureddr {
)dd{ |
{ee 
tryff 
{gg 	
unsafehh 
{ii 
fixedjj 
(jj 
bytejj 
*jj 
dataPtrjj $
=jj% &
datajj' +
)jj+ ,
fixedkk 
(kk 
bytekk 
*kk 
signaturePtrkk )
=kk* +
	signaturekk, 5
)kk5 6
{ll *
CertificatePinningNativeResultmm 2
resultmm3 9
=mm: ;+
CertificatePinningNativeLibrarymm< [
.mm[ \
VerifySignaturemm\ k
(mmk l
dataPtrnn 
,nn  
(nn! "
nuintnn" '
)nn' (
datann( ,
.nn, -
Lengthnn- 3
,nn3 4
signaturePtroo $
,oo$ %
(oo& '
nuintoo' ,
)oo, -
	signatureoo- 6
.oo6 7
Lengthoo7 =
)oo= >
;oo> ?
returnqq 
resultqq !
switchqq" (
{rr *
CertificatePinningNativeResultss 6
.ss6 7
Successss7 >
=>ss? A(
CertificatePinningBoolResultssB ^
.ss^ _
	FromValuess_ h
(ssh i
truessi m
)ssm n
,ssn o*
CertificatePinningNativeResulttt 6
.tt6 7#
ErrorVerificationFailedtt7 N
=>ttO Q(
CertificatePinningBoolResultttR n
.ttn o
	FromValuetto x
(ttx y
falsetty ~
)tt~ 
,	tt Ä
_uu 
=>uu (
CertificatePinningBoolResultuu 9
.uu9 :
	FromErroruu: C
(uuC D%
CertificatePinningFailurevv 5
.vv5 6'
ED_25519_VERIFICATION_ERRORvv6 Q
(vvQ R 
GetErrorStringStaticvvR f
(vvf g
resultvvg m
)vvm n
)vvn o
)vvo p
}ww 
;ww 
}xx 
}yy 
}zz 	
catch{{ 
({{ 
	Exception{{ 
ex{{ 
){{ 
{|| 	
return}} (
CertificatePinningBoolResult}} /
.}}/ 0
	FromError}}0 9
(}}9 :%
CertificatePinningFailure}}: S
.}}S T+
ED_25519_VERIFICATION_EXCEPTION}}T s
(}}s t
ex}}t v
)}}v w
)}}w x
;}}x y
}~~ 	
} 
public
ÅÅ 
/
!CertificatePinningByteArrayResult
ÅÅ ,
Encrypt
ÅÅ- 4
(
ÅÅ4 5
ReadOnlyMemory
ÇÇ 
<
ÇÇ 
byte
ÇÇ 
>
ÇÇ 
	plaintext
ÇÇ &
)
ÇÇ& '
{
ÉÉ /
!CertificatePinningOperationResult
ÑÑ )

stateCheck
ÑÑ* 4
=
ÑÑ5 6$
ValidateOperationState
ÑÑ7 M
(
ÑÑM N
)
ÑÑN O
;
ÑÑO P
if
ÖÖ 

(
ÖÖ 
!
ÖÖ 

stateCheck
ÖÖ 
.
ÖÖ 
	IsSuccess
ÖÖ !
)
ÖÖ! "
{
ÜÜ 	
return
áá /
!CertificatePinningByteArrayResult
áá 4
.
áá4 5
	FromError
áá5 >
(
áá> ?

stateCheck
áá? I
.
ááI J
ERROR
ááJ O
!
ááO P
)
ááP Q
;
ááQ R
}
àà 	
if
ää 

(
ää 
	plaintext
ää 
.
ää 
IsEmpty
ää 
)
ää 
{
ãã 	
return
åå /
!CertificatePinningByteArrayResult
åå 4
.
åå4 5
	FromError
åå5 >
(
åå> ?'
CertificatePinningFailure
åå? X
.
ååX Y 
PLAINTEXT_REQUIRED
ååY k
(
ååk l
)
åål m
)
ååm n
;
åån o
}
çç 	
return
èè 
EncryptUnsafe
èè 
(
èè 
	plaintext
èè &
.
èè& '
Span
èè' +
)
èè+ ,
;
èè, -
}
êê 
private
ìì 
static
ìì /
!CertificatePinningByteArrayResult
ìì 4
EncryptUnsafe
ìì5 B
(
ììB C
ReadOnlySpan
ììC O
<
ììO P
byte
ììP T
>
ììT U
	plaintext
ììV _
)
ìì_ `
{
îî 
try
ïï 
{
ññ 	
unsafe
óó 
{
òò 
fixed
ôô 
(
ôô 
byte
ôô 
*
ôô 
plaintextPtr
ôô )
=
ôô* +
	plaintext
ôô, 5
)
ôô5 6
{
öö 
const
õõ 
nuint
õõ 
MAX_STACK_SIZE
õõ  .
=
õõ/ 0
$num
õõ1 5
;
õõ5 6
nuint
úú 
estimatedSize
úú '
=
úú( )
(
úú* +
nuint
úú+ 0
)
úú0 1
	plaintext
úú1 :
.
úú: ;
Length
úú; A
+
úúB C
$num
úúD G
;
úúG H
if
ûû 
(
ûû 
estimatedSize
ûû %
<=
ûû& (
MAX_STACK_SIZE
ûû) 7
)
ûû7 8
{
üü 
byte
†† 
*
†† 
stackBuffer
†† )
=
††* +

stackalloc
††, 6
byte
††7 ;
[
††; <
(
††< =
int
††= @
)
††@ A
estimatedSize
††A N
]
††N O
;
††O P
nuint
°° 

actualSize
°° (
=
°°) *
estimatedSize
°°+ 8
;
°°8 9,
CertificatePinningNativeResult
££ 6
result
££7 =
=
££> ?-
CertificatePinningNativeLibrary
££@ _
.
££_ `
Encrypt
££` g
(
££g h
plaintextPtr
§§ (
,
§§( )
(
§§* +
nuint
§§+ 0
)
§§0 1
	plaintext
§§1 :
.
§§: ;
Length
§§; A
,
§§A B
stackBuffer
•• '
,
••' (
&
••) *

actualSize
••* 4
)
••4 5
;
••5 6
if
ßß 
(
ßß 
result
ßß "
==
ßß# %,
CertificatePinningNativeResult
ßß& D
.
ßßD E
Success
ßßE L
)
ßßL M
{
®® 
byte
©©  
[
©©  !
]
©©! "
output
©©# )
=
©©* +
new
©©, /
byte
©©0 4
[
©©4 5

actualSize
©©5 ?
]
©©? @
;
©©@ A
fixed
™™ !
(
™™" #
byte
™™# '
*
™™' (
	outputPtr
™™) 2
=
™™3 4
output
™™5 ;
)
™™; <
{
´´ 
Buffer
¨¨  &
.
¨¨& '

MemoryCopy
¨¨' 1
(
¨¨1 2
stackBuffer
¨¨2 =
,
¨¨= >
	outputPtr
¨¨? H
,
¨¨H I

actualSize
¨¨J T
,
¨¨T U

actualSize
¨¨V `
)
¨¨` a
;
¨¨a b
}
≠≠ 
return
ÆÆ "/
!CertificatePinningByteArrayResult
ÆÆ# D
.
ÆÆD E
	FromValue
ÆÆE N
(
ÆÆN O
output
ÆÆO U
)
ÆÆU V
;
ÆÆV W
}
ØØ 
return
±± /
!CertificatePinningByteArrayResult
±± @
.
±±@ A
	FromError
±±A J
(
±±J K'
CertificatePinningFailure
≤≤ 5
.
≤≤5 6#
RSA_ENCRYPTION_FAILED
≤≤6 K
(
≤≤K L"
GetErrorStringStatic
≤≤L `
(
≤≤` a
result
≤≤a g
)
≤≤g h
)
≤≤h i
)
≤≤i j
;
≤≤j k
}
≥≥ 
else
¥¥ 
{
µµ 
byte
∂∂ 
[
∂∂ 
]
∂∂ 

ciphertext
∂∂ )
=
∂∂* +
new
∂∂, /
byte
∂∂0 4
[
∂∂4 5
estimatedSize
∂∂5 B
]
∂∂B C
;
∂∂C D
nuint
∑∑ 

actualSize
∑∑ (
=
∑∑) *
estimatedSize
∑∑+ 8
;
∑∑8 9
fixed
ππ 
(
ππ 
byte
ππ #
*
ππ# $
ciphertextPtr
ππ% 2
=
ππ3 4

ciphertext
ππ5 ?
)
ππ? @
{
∫∫ ,
CertificatePinningNativeResult
ªª :
result
ªª; A
=
ªªB C-
CertificatePinningNativeLibrary
ªªD c
.
ªªc d
Encrypt
ªªd k
(
ªªk l
plaintextPtr
ºº  ,
,
ºº, -
(
ºº. /
nuint
ºº/ 4
)
ºº4 5
	plaintext
ºº5 >
.
ºº> ?
Length
ºº? E
,
ººE F
ciphertextPtr
ΩΩ  -
,
ΩΩ- .
&
ΩΩ/ 0

actualSize
ΩΩ0 :
)
ΩΩ: ;
;
ΩΩ; <
if
øø 
(
øø  
result
øø  &
==
øø' ),
CertificatePinningNativeResult
øø* H
.
øøH I
Success
øøI P
)
øøP Q
{
¿¿ 
if
¡¡  "
(
¡¡# $

actualSize
¡¡$ .
!=
¡¡/ 1
estimatedSize
¡¡2 ?
)
¡¡? @
{
¬¬  !
byte
√√$ (
[
√√( )
]
√√) *
resized
√√+ 2
=
√√3 4
new
√√5 8
byte
√√9 =
[
√√= >

actualSize
√√> H
]
√√H I
;
√√I J
Array
ƒƒ$ )
.
ƒƒ) *
Copy
ƒƒ* .
(
ƒƒ. /

ciphertext
ƒƒ/ 9
,
ƒƒ9 :
resized
ƒƒ; B
,
ƒƒB C
(
ƒƒD E
int
ƒƒE H
)
ƒƒH I

actualSize
ƒƒI S
)
ƒƒS T
;
ƒƒT U
return
≈≈$ */
!CertificatePinningByteArrayResult
≈≈+ L
.
≈≈L M
	FromValue
≈≈M V
(
≈≈V W
resized
≈≈W ^
)
≈≈^ _
;
≈≈_ `
}
∆∆  !
return
««  &/
!CertificatePinningByteArrayResult
««' H
.
««H I
	FromValue
««I R
(
««R S

ciphertext
««S ]
)
««] ^
;
««^ _
}
»» 
return
   "/
!CertificatePinningByteArrayResult
  # D
.
  D E
	FromError
  E N
(
  N O'
CertificatePinningFailure
ÀÀ  9
.
ÀÀ9 :#
RSA_ENCRYPTION_FAILED
ÀÀ: O
(
ÀÀO P"
GetErrorStringStatic
ÀÀP d
(
ÀÀd e
result
ÀÀe k
)
ÀÀk l
)
ÀÀl m
)
ÀÀm n
;
ÀÀn o
}
ÃÃ 
}
ÕÕ 
}
ŒŒ 
}
œœ 
}
–– 	
catch
—— 
(
—— 
	Exception
—— 
ex
—— 
)
—— 
{
““ 	
return
”” /
!CertificatePinningByteArrayResult
”” 4
.
””4 5
	FromError
””5 >
(
””> ?'
CertificatePinningFailure
””? X
.
””X Y&
RSA_ENCRYPTION_EXCEPTION
””Y q
(
””q r
ex
””r t
)
””t u
)
””u v
;
””v w
}
‘‘ 	
}
’’ 
public
◊◊ 
/
!CertificatePinningByteArrayResult
◊◊ ,
Decrypt
◊◊- 4
(
◊◊4 5
ReadOnlyMemory
ÿÿ 
<
ÿÿ 
byte
ÿÿ 
>
ÿÿ 

ciphertext
ÿÿ '
)
ÿÿ' (
{
ŸŸ /
!CertificatePinningOperationResult
⁄⁄ )

stateCheck
⁄⁄* 4
=
⁄⁄5 6$
ValidateOperationState
⁄⁄7 M
(
⁄⁄M N
)
⁄⁄N O
;
⁄⁄O P
if
€€ 

(
€€ 
!
€€ 

stateCheck
€€ 
.
€€ 
	IsSuccess
€€ !
)
€€! "
{
‹‹ 	
return
›› /
!CertificatePinningByteArrayResult
›› 4
.
››4 5
	FromError
››5 >
(
››> ?

stateCheck
››? I
.
››I J
ERROR
››J O
!
››O P
)
››P Q
;
››Q R
}
ﬁﬁ 	
if
‡‡ 

(
‡‡ 

ciphertext
‡‡ 
.
‡‡ 
IsEmpty
‡‡ 
)
‡‡ 
{
·· 	
return
‚‚ /
!CertificatePinningByteArrayResult
‚‚ 4
.
‚‚4 5
	FromError
‚‚5 >
(
‚‚> ?'
CertificatePinningFailure
‚‚? X
.
‚‚X Y!
CIPHERTEXT_REQUIRED
‚‚Y l
(
‚‚l m
)
‚‚m n
)
‚‚n o
;
‚‚o p
}
„„ 	
return
ÂÂ 
DecryptUnsafe
ÂÂ 
(
ÂÂ 

ciphertext
ÂÂ '
.
ÂÂ' (
Span
ÂÂ( ,
)
ÂÂ, -
;
ÂÂ- .
}
ÊÊ 
private
ÈÈ 
static
ÈÈ /
!CertificatePinningByteArrayResult
ÈÈ 4
DecryptUnsafe
ÈÈ5 B
(
ÈÈB C
ReadOnlySpan
ÈÈC O
<
ÈÈO P
byte
ÈÈP T
>
ÈÈT U

ciphertext
ÈÈV `
)
ÈÈ` a
{
ÍÍ 
try
ÎÎ 
{
ÏÏ 	
unsafe
ÌÌ 
{
ÓÓ 
fixed
ÔÔ 
(
ÔÔ 
byte
ÔÔ 
*
ÔÔ 
ciphertextPtr
ÔÔ *
=
ÔÔ+ ,

ciphertext
ÔÔ- 7
)
ÔÔ7 8
{
 
nuint
ÒÒ 
plaintextLen
ÒÒ &
=
ÒÒ' (
(
ÒÒ) *
nuint
ÒÒ* /
)
ÒÒ/ 0

ciphertext
ÒÒ0 :
.
ÒÒ: ;
Length
ÒÒ; A
;
ÒÒA B
byte
ÚÚ 
[
ÚÚ 
]
ÚÚ 
	plaintext
ÚÚ $
=
ÚÚ% &
new
ÚÚ' *
byte
ÚÚ+ /
[
ÚÚ/ 0
plaintextLen
ÚÚ0 <
]
ÚÚ< =
;
ÚÚ= >
fixed
ÙÙ 
(
ÙÙ 
byte
ÙÙ 
*
ÙÙ  
plaintextPtr
ÙÙ! -
=
ÙÙ. /
	plaintext
ÙÙ0 9
)
ÙÙ9 :
{
ıı ,
CertificatePinningNativeResult
ˆˆ 6
result
ˆˆ7 =
=
ˆˆ> ?-
CertificatePinningNativeLibrary
ˆˆ@ _
.
ˆˆ_ `
Decrypt
ˆˆ` g
(
ˆˆg h
ciphertextPtr
˜˜ )
,
˜˜) *
(
˜˜+ ,
nuint
˜˜, 1
)
˜˜1 2

ciphertext
˜˜2 <
.
˜˜< =
Length
˜˜= C
,
˜˜C D
plaintextPtr
¯¯ (
,
¯¯( )
&
¯¯* +
plaintextLen
¯¯+ 7
)
¯¯7 8
;
¯¯8 9
if
˙˙ 
(
˙˙ 
result
˙˙ "
==
˙˙# %,
CertificatePinningNativeResult
˙˙& D
.
˙˙D E
Success
˙˙E L
)
˙˙L M
{
˚˚ 
if
¸¸ 
(
¸¸  
plaintextLen
¸¸  ,
!=
¸¸- /
(
¸¸0 1
nuint
¸¸1 6
)
¸¸6 7

ciphertext
¸¸7 A
.
¸¸A B
Length
¸¸B H
)
¸¸H I
{
˝˝ 
byte
˛˛  $
[
˛˛$ %
]
˛˛% &
resized
˛˛' .
=
˛˛/ 0
new
˛˛1 4
byte
˛˛5 9
[
˛˛9 :
plaintextLen
˛˛: F
]
˛˛F G
;
˛˛G H
Array
ˇˇ  %
.
ˇˇ% &
Copy
ˇˇ& *
(
ˇˇ* +
	plaintext
ˇˇ+ 4
,
ˇˇ4 5
resized
ˇˇ6 =
,
ˇˇ= >
(
ˇˇ? @
int
ˇˇ@ C
)
ˇˇC D
plaintextLen
ˇˇD P
)
ˇˇP Q
;
ˇˇQ R
return
ÄÄ  &/
!CertificatePinningByteArrayResult
ÄÄ' H
.
ÄÄH I
	FromValue
ÄÄI R
(
ÄÄR S
resized
ÄÄS Z
)
ÄÄZ [
;
ÄÄ[ \
}
ÅÅ 
return
ÇÇ "/
!CertificatePinningByteArrayResult
ÇÇ# D
.
ÇÇD E
	FromValue
ÇÇE N
(
ÇÇN O
	plaintext
ÇÇO X
)
ÇÇX Y
;
ÇÇY Z
}
ÉÉ 
return
ÖÖ /
!CertificatePinningByteArrayResult
ÖÖ @
.
ÖÖ@ A
	FromError
ÖÖA J
(
ÖÖJ K'
CertificatePinningFailure
ÜÜ 5
.
ÜÜ5 6#
RSA_DECRYPTION_FAILED
ÜÜ6 K
(
ÜÜK L"
GetErrorStringStatic
ÜÜL `
(
ÜÜ` a
result
ÜÜa g
)
ÜÜg h
)
ÜÜh i
)
ÜÜi j
;
ÜÜj k
}
áá 
}
àà 
}
ââ 
}
ää 	
catch
ãã 
(
ãã 
	Exception
ãã 
ex
ãã 
)
ãã 
{
åå 	
return
çç /
!CertificatePinningByteArrayResult
çç 4
.
çç4 5
	FromError
çç5 >
(
çç> ?'
CertificatePinningFailure
çç? X
.
ççX Y&
RSA_DECRYPTION_EXCEPTION
ççY q
(
ççq r
ex
ççr t
)
ççt u
)
ççu v
;
ççv w
}
éé 	
}
èè 
public
ëë 
/
!CertificatePinningByteArrayResult
ëë ,
GetPublicKey
ëë- 9
(
ëë9 :
)
ëë: ;
{
íí /
!CertificatePinningOperationResult
ìì )

stateCheck
ìì* 4
=
ìì5 6$
ValidateOperationState
ìì7 M
(
ììM N
)
ììN O
;
ììO P
if
îî 

(
îî 
!
îî 

stateCheck
îî 
.
îî 
	IsSuccess
îî !
)
îî! "
{
ïï 	
return
ññ /
!CertificatePinningByteArrayResult
ññ 4
.
ññ4 5
	FromError
ññ5 >
(
ññ> ?

stateCheck
ññ? I
.
ññI J
ERROR
ññJ O
!
ññO P
)
ññP Q
;
ññQ R
}
óó 	
return
ôô  
GetPublicKeyUnsafe
ôô !
(
ôô! "
)
ôô" #
;
ôô# $
}
öö 
private
ùù 
static
ùù /
!CertificatePinningByteArrayResult
ùù 4 
GetPublicKeyUnsafe
ùù5 G
(
ùùG H
)
ùùH I
{
ûû 
try
üü 
{
†† 	
unsafe
°° 
{
¢¢ 
const
££ 
nuint
££ %
INITIAL_KEY_BUFFER_SIZE
££ 3
=
££4 5
$num
££6 :
;
££: ;
nuint
§§ 
keyLen
§§ 
=
§§ %
INITIAL_KEY_BUFFER_SIZE
§§ 6
;
§§6 7
byte
•• 
[
•• 
]
•• 
	publicKey
••  
=
••! "
new
••# &
byte
••' +
[
••+ ,
keyLen
••, 2
]
••2 3
;
••3 4
fixed
ßß 
(
ßß 
byte
ßß 
*
ßß 
keyPtr
ßß #
=
ßß$ %
	publicKey
ßß& /
)
ßß/ 0
{
®® ,
CertificatePinningNativeResult
©© 2
result
©©3 9
=
©©: ;-
CertificatePinningNativeLibrary
©©< [
.
©©[ \
GetPublicKey
©©\ h
(
©©h i
keyPtr
©©i o
,
©©o p
&
©©q r
keyLen
©©r x
)
©©x y
;
©©y z
if
´´ 
(
´´ 
result
´´ 
==
´´ !,
CertificatePinningNativeResult
´´" @
.
´´@ A
Success
´´A H
)
´´H I
{
¨¨ 
if
≠≠ 
(
≠≠ 
keyLen
≠≠ "
!=
≠≠# %%
INITIAL_KEY_BUFFER_SIZE
≠≠& =
)
≠≠= >
{
ÆÆ 
byte
ØØ  
[
ØØ  !
]
ØØ! "
resized
ØØ# *
=
ØØ+ ,
new
ØØ- 0
byte
ØØ1 5
[
ØØ5 6
keyLen
ØØ6 <
]
ØØ< =
;
ØØ= >
Array
∞∞ !
.
∞∞! "
Copy
∞∞" &
(
∞∞& '
	publicKey
∞∞' 0
,
∞∞0 1
resized
∞∞2 9
,
∞∞9 :
(
∞∞; <
int
∞∞< ?
)
∞∞? @
keyLen
∞∞@ F
)
∞∞F G
;
∞∞G H
return
±± "/
!CertificatePinningByteArrayResult
±±# D
.
±±D E
	FromValue
±±E N
(
±±N O
resized
±±O V
)
±±V W
;
±±W X
}
≤≤ 
return
≥≥ /
!CertificatePinningByteArrayResult
≥≥ @
.
≥≥@ A
	FromValue
≥≥A J
(
≥≥J K
	publicKey
≥≥K T
)
≥≥T U
;
≥≥U V
}
¥¥ 
return
∂∂ /
!CertificatePinningByteArrayResult
∂∂ <
.
∂∂< =
	FromError
∂∂= F
(
∂∂F G'
CertificatePinningFailure
∑∑ 1
.
∑∑1 2+
CERTIFICATE_VALIDATION_FAILED
∑∑2 O
(
∑∑O P"
GetErrorStringStatic
∑∑P d
(
∑∑d e
result
∑∑e k
)
∑∑k l
)
∑∑l m
)
∑∑m n
;
∑∑n o
}
∏∏ 
}
ππ 
}
∫∫ 	
catch
ªª 
(
ªª 
	Exception
ªª 
ex
ªª 
)
ªª 
{
ºº 	
return
ΩΩ /
!CertificatePinningByteArrayResult
ΩΩ 4
.
ΩΩ4 5
	FromError
ΩΩ5 >
(
ΩΩ> ?'
CertificatePinningFailure
ΩΩ? X
.
ΩΩX Y.
 CERTIFICATE_VALIDATION_EXCEPTION
ΩΩY y
(
ΩΩy z
ex
ΩΩz |
)
ΩΩ| }
)
ΩΩ} ~
;
ΩΩ~ 
}
ææ 	
}
øø 
private
¡¡ /
!CertificatePinningOperationResult
¡¡ -$
ValidateOperationState
¡¡. D
(
¡¡D E
)
¡¡E F
{
¬¬ 
return
√√ 
_state
√√ 
switch
√√ 
{
ƒƒ 	
DISPOSED
≈≈ 
=>
≈≈ /
!CertificatePinningOperationResult
≈≈ 9
.
≈≈9 :
	FromError
≈≈: C
(
≈≈C D'
CertificatePinningFailure
≈≈D ]
.
≈≈] ^
SERVICE_DISPOSED
≈≈^ n
(
≈≈n o
)
≈≈o p
)
≈≈p q
,
≈≈q r
NOT_INITIALIZED
∆∆ 
=>
∆∆ /
!CertificatePinningOperationResult
∆∆ @
.
∆∆@ A
	FromError
∆∆A J
(
∆∆J K'
CertificatePinningFailure
∆∆K d
.
∆∆d e%
SERVICE_NOT_INITIALIZED
∆∆e |
(
∆∆| }
)
∆∆} ~
)
∆∆~ 
,∆∆ Ä
INITIALIZING
«« 
=>
«« /
!CertificatePinningOperationResult
«« =
.
««= >
	FromError
««> G
(
««G H'
CertificatePinningFailure
««H a
.
««a b"
SERVICE_INITIALIZING
««b v
(
««v w
)
««w x
)
««x y
,
««y z
INITIALIZED
»» 
=>
»» /
!CertificatePinningOperationResult
»» <
.
»»< =
Success
»»= D
(
»»D E
)
»»E F
,
»»F G
_
…… 
=>
…… /
!CertificatePinningOperationResult
…… 2
.
……2 3
	FromError
……3 <
(
……< ='
CertificatePinningFailure
……= V
.
……V W#
SERVICE_INVALID_STATE
……W l
(
……l m
)
……m n
)
……n o
}
   	
;
  	 

}
ÀÀ 
private
ÕÕ 
static
ÕÕ 
unsafe
ÕÕ 
string
ÕÕ  "
GetErrorStringStatic
ÕÕ! 5
(
ÕÕ5 6,
CertificatePinningNativeResult
ÕÕ6 T
result
ÕÕU [
)
ÕÕ[ \
{
ŒŒ 
try
œœ 
{
–– 	
byte
—— 
*
—— 
errorPtr
—— 
=
—— -
CertificatePinningNativeLibrary
—— <
.
——< =
GetErrorMessage
——= L
(
——L M
)
——M N
;
——N O
if
““ 
(
““ 
errorPtr
““ 
!=
““ 
null
““  
)
““  !
{
”” 
return
‘‘ 
Marshal
‘‘ 
.
‘‘ 
PtrToStringUTF8
‘‘ .
(
‘‘. /
(
‘‘/ 0
IntPtr
‘‘0 6
)
‘‘6 7
errorPtr
‘‘7 ?
)
‘‘? @
??
‘‘A C
FormattableString
‘‘D U
.
‘‘U V
	Invariant
‘‘V _
(
‘‘_ `
$"
‘‘` b
$str
‘‘b q
{
‘‘q r
result
‘‘r x
}
‘‘x y
"
‘‘y z
)
‘‘z {
;
‘‘{ |
}
’’ 
}
÷÷ 	
catch
◊◊ 
(
◊◊ 
	Exception
◊◊ 
ex
◊◊ 
)
◊◊ 
{
ÿÿ 	
Serilog
ŸŸ 
.
ŸŸ 
Log
ŸŸ 
.
ŸŸ 
Warning
ŸŸ 
(
ŸŸ  
ex
ŸŸ  "
,
ŸŸ" #
$str
ŸŸ$ u
,
ŸŸu v
result
⁄⁄ 
)
⁄⁄ 
;
⁄⁄ 
}
€€ 	
return
›› 
FormattableString
››  
.
››  !
	Invariant
››! *
(
››* +
$"
››+ -
$str
››- 9
{
››9 :
result
››: @
}
››@ A
"
››A B
)
››B C
;
››C D
}
ﬁﬁ 
public
‡‡ 

async
‡‡ 
	ValueTask
‡‡ 
DisposeAsync
‡‡ '
(
‡‡' (
)
‡‡( )
{
·· 
if
‚‚ 

(
‚‚ 
Interlocked
‚‚ 
.
‚‚ 
Exchange
‚‚  
(
‚‚  !
ref
‚‚! $
_state
‚‚% +
,
‚‚+ ,
DISPOSED
‚‚- 5
)
‚‚5 6
==
‚‚7 9
DISPOSED
‚‚: B
)
‚‚B C
{
„„ 	
return
‰‰ 
;
‰‰ 
}
ÂÂ 	
try
ÁÁ 
{
ËË 	
await
ÈÈ 
Task
ÈÈ 
.
ÈÈ 
Run
ÈÈ 
(
ÈÈ 
static
ÈÈ !
(
ÈÈ" #
)
ÈÈ# $
=>
ÈÈ% '
{
ÍÍ 
try
ÎÎ 
{
ÏÏ -
CertificatePinningNativeLibrary
ÌÌ 3
.
ÌÌ3 4
Cleanup
ÌÌ4 ;
(
ÌÌ; <
)
ÌÌ< =
;
ÌÌ= >
}
ÓÓ 
catch
ÔÔ 
(
ÔÔ 
	Exception
ÔÔ  
ex
ÔÔ! #
)
ÔÔ# $
{
 
Serilog
ÒÒ 
.
ÒÒ 
Log
ÒÒ 
.
ÒÒ  
Warning
ÒÒ  '
(
ÒÒ' (
ex
ÒÒ( *
,
ÒÒ* +
$str
ÒÒ, i
)
ÒÒi j
;
ÒÒj k
}
ÚÚ 
}
ÛÛ 
)
ÛÛ 
.
ÛÛ 
ConfigureAwait
ÛÛ 
(
ÛÛ 
false
ÛÛ #
)
ÛÛ# $
;
ÛÛ$ %
}
ÙÙ 	
finally
ıı 
{
ˆˆ 	!
_initializationLock
˜˜ 
.
˜˜  
Dispose
˜˜  '
(
˜˜' (
)
˜˜( )
;
˜˜) *
}
¯¯ 	
}
˘˘ 
}˙˙ ·
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
 ÂR
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
(55 
File55 
.55 
Exists55 
(55 
libPath55 #
)55# $
)55$ %
{66 
try77 
{88 
return99 
NativeLibrary99 (
.99( )
Load99) -
(99- .
libPath99. 5
)995 6
;996 7
}:: 
catch;; 
(;; 
	Exception;;  
);;  !
{<< 
}>> 
}?? 
}@@ 	
returnBB 
IntPtrBB 
.BB 
ZeroBB 
;BB 
}CC 
privateEE 
staticEE 
stringEE  
GetRuntimeIdentifierEE .
(EE. /
)EE/ 0
{FF 
ifGG 

(GG 
RuntimeInformationGG 
.GG 
IsOSPlatformGG +
(GG+ ,

OSPlatformGG, 6
.GG6 7
WindowsGG7 >
)GG> ?
)GG? @
{HH 	
returnII 
RuntimeInformationII %
.II% &
ProcessArchitectureII& 9
==II: <
ArchitectureII= I
.III J
X86IIJ M
?IIN O
$strIIP Y
:IIZ [
$strII\ e
;IIe f
}JJ 	
ifKK 

(KK 
RuntimeInformationKK 
.KK 
IsOSPlatformKK +
(KK+ ,

OSPlatformKK, 6
.KK6 7
OSXKK7 :
)KK: ;
)KK; <
{LL 	
returnMM 
RuntimeInformationMM %
.MM% &
ProcessArchitectureMM& 9
==MM: <
ArchitectureMM= I
.MMI J
Arm64MMJ O
?MMP Q
$strMMR ]
:MM^ _
$strMM` i
;MMi j
}NN 	
returnOO 
$strOO 
;OO 
}PP 
[RR 
	DllImportRR 
(RR 
LIBRARY_NAMERR 
,RR 

EntryPointRR '
=RR( )
$strRR* @
,RR@ A
CallingConventionRRB S
=RRT U
CallingConventionRRV g
.RRg h
CdeclRRh m
)RRm n
]RRn o
publicSS 

staticSS 
externSS *
CertificatePinningNativeResultSS 7

InitializeSS8 B
(SSB C
)SSC D
;SSD E
[UU 
	DllImportUU 
(UU 
LIBRARY_NAMEUU 
,UU 

EntryPointUU '
=UU( )
$strUU* C
,UUC D
CallingConventionUUE V
=UUW X
CallingConventionUUY j
.UUj k
CdeclUUk p
)UUp q
]UUq r
publicVV 

staticVV 
externVV 
voidVV 
CleanupVV %
(VV% &
)VV& '
;VV' (
[XX 
	DllImportXX 
(XX 
LIBRARY_NAMEXX 
,XX 

EntryPointXX '
=XX( )
$strXX* B
,XXB C
CallingConventionXXD U
=XXV W
CallingConventionXXX i
.XXi j
CdeclXXj o
)XXo p
]XXp q
publicYY 

staticYY 
externYY *
CertificatePinningNativeResultYY 7
VerifySignatureYY8 G
(YYG H
byteZZ 
*ZZ 
dataZZ 
,ZZ 
nuintZZ 
dataLenZZ !
,ZZ! "
byte[[ 
*[[ 
	signature[[ 
,[[ 
nuint[[ 
signatureLen[[ +
)[[+ ,
;[[, -
[]] 
	DllImport]] 
(]] 
LIBRARY_NAME]] 
,]] 

EntryPoint]] '
=]]( )
$str]]* C
,]]C D
CallingConvention]]E V
=]]W X
CallingConvention]]Y j
.]]j k
Cdecl]]k p
)]]p q
]]]q r
public^^ 

static^^ 
extern^^ *
CertificatePinningNativeResult^^ 7
Encrypt^^8 ?
(^^? @
byte__ 
*__ 
	plaintext__ 
,__ 
nuint__ 
plaintextLen__ +
,__+ ,
byte`` 
*`` 

ciphertext`` 
,`` 
nuint`` 
*``  
ciphertextLen``! .
)``. /
;``/ 0
[bb 
	DllImportbb 
(bb 
LIBRARY_NAMEbb 
,bb 

EntryPointbb '
=bb( )
$strbb* C
,bbC D
CallingConventionbbE V
=bbW X
CallingConventionbbY j
.bbj k
Cdeclbbk p
)bbp q
]bbq r
publiccc 

staticcc 
externcc *
CertificatePinningNativeResultcc 7
Decryptcc8 ?
(cc? @
bytedd 
*dd 

ciphertextdd 
,dd 
nuintdd 
ciphertextLendd  -
,dd- .
byteee 
*ee 
	plaintextee 
,ee 
nuintee 
*ee 
plaintextLenee  ,
)ee, -
;ee- .
[gg 
	DllImportgg 
(gg 
LIBRARY_NAMEgg 
,gg 

EntryPointgg '
=gg( )
$strgg* J
,ggJ K
CallingConventionggL ]
=gg^ _
CallingConventiongg` q
.ggq r
Cdeclggr w
)ggw x
]ggx y
publichh 

statichh 
externhh *
CertificatePinningNativeResulthh 7
GetPublicKeyhh8 D
(hhD E
byteii 
*ii 
publicKeyDerii 
,ii 
nuintii !
*ii! "
publicKeyLenii# /
)ii/ 0
;ii0 1
[kk 
	DllImportkk 
(kk 
LIBRARY_NAMEkk 
,kk 

EntryPointkk '
=kk( )
$strkk* E
,kkE F
CallingConventionkkG X
=kkY Z
CallingConventionkk[ l
.kkl m
Cdeclkkm r
)kkr s
]kks t
publicll 

staticll 
externll 
bytell 
*ll 
GetErrorMessagell .
(ll. /
)ll/ 0
;ll0 1
}mm ÷
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