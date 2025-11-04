≥˙
i/Users/oleksandrmelnychenko/RiderProjects/ecliptix-desktop/Ecliptix.Core/Ecliptix.Core.Desktop/Program.cs
	namespaceJJ 	
EcliptixJJ
 
.JJ 
CoreJJ 
.JJ 
DesktopJJ 
;JJ  
publicLL 
staticLL 
classLL 
ProgramLL 
{MM 
[NN 
	STAThreadNN 
]NN 
publicOO 

staticOO 
asyncOO 
TaskOO 
MainOO !
(OO! "
stringOO" (
[OO( )
]OO) *
argsOO+ /
)OO/ 0
{PP 
stringQQ 
	mutexNameQQ 
=QQ 
stringRR 
.RR 
FormatRR 
(RR  
ApplicationConstantsRR .
.RR. /
ApplicationSettingsRR/ B
.RRB C
MUTEX_NAME_FORMATRRC T
,RRT U
EnvironmentRRV a
.RRa b
UserNameRRb j
)RRj k
;RRk l
usingSS 
MutexSS 
mutexSS 
=SS 
newSS 
(SS  
trueSS  $
,SS$ %
	mutexNameSS& /
,SS/ 0
outSS1 4
boolSS5 9

createdNewSS: D
)SSD E
;SSE F
ifUU 

(UU 
!UU 

createdNewUU 
)UU 
{VV 	
returnWW 
;WW 
}XX 	
IConfigurationZZ 
configurationZZ $
=ZZ% &
BuildConfigurationZZ' 9
(ZZ9 :
)ZZ: ;
;ZZ; <
Env[[ 
.[[ 
Load[[ 
([[ 
)[[ 
;[[ 
Log\\ 
.\\ 
Logger\\ 
=\\ 
ConfigureSerilog\\ %
(\\% &
configuration\\& 3
)\\3 4
;\\4 5
try^^ 
{__ 	
Log`` 
.`` 
Information`` 
(``  
ApplicationConstants`` 0
.``0 1
Logging``1 8
.``8 9
STARTUP_MESSAGE``9 H
)``H I
;``I J
IServiceCollectionaa 
servicesaa '
=aa( )
ConfigureServicesaa* ;
(aa; <
configurationaa< I
)aaI J
;aaJ K
servicescc 
.cc *
UseMicrosoftDependencyResolvercc 3
(cc3 4
)cc4 5
;cc5 6
IServiceProvideree 
serviceProvideree ,
=ee- .
servicesee/ 7
.ee7 8 
BuildServiceProvideree8 L
(eeL M
)eeM N
;eeN O

ReactiveUIff 
.ff 
IViewLocatorff #
reactiveViewLocatorff$ 7
=ff8 9
serviceProviderff: I
.ffI J
GetRequiredServiceffJ \
<ff\ ]

ReactiveUIff] g
.ffg h
IViewLocatorffh t
>fft u
(ffu v
)ffv w
;ffw x
Splatgg 
.gg 
Locatorgg 
.gg 
CurrentMutablegg (
.gg( )
Registergg) 1
(gg1 2
(gg2 3
)gg3 4
=>gg5 7
reactiveViewLocatorgg8 K
,ggK L
typeofggM S
(ggS T

ReactiveUIggT ^
.gg^ _
IViewLocatorgg_ k
)ggk l
)ggl m
;ggm n
BuildAvaloniaAppii 
(ii 
)ii 
.ii +
StartWithClassicDesktopLifetimeii >
(ii> ?
argsii? C
)iiC D
;iiD E
}jj 	
catchkk 
(kk 
	Exceptionkk 
exkk 
)kk 
{ll 	
Logmm 
.mm 
Fatalmm 
(mm 
exmm 
,mm  
ApplicationConstantsmm .
.mm. /
Loggingmm/ 6
.mm6 7
FATAL_ERROR_MESSAGEmm7 J
)mmJ K
;mmK L
ifnn 
(nn 
configurationnn 
[nn  
ApplicationConstantsnn 2
.nn2 3
ApplicationSettingsnn3 F
.nnF G
ENVIRONMENT_KEYnnG V
]nnV W
!=nnX Z 
ApplicationConstantsoo $
.oo$ %
ApplicationSettingsoo% 8
.oo8 9#
DEVELOPMENT_ENVIRONMENToo9 P
)ooP Q
{pp 
Environmentqq 
.qq 
Exitqq  
(qq  ! 
ApplicationConstantsqq! 5
.qq5 6
	ExitCodesqq6 ?
.qq? @
FATAL_ERRORqq@ K
)qqK L
;qqL M
}rr 
}ss 	
finallytt 
{uu 	
Logvv 
.vv 
Informationvv 
(vv  
ApplicationConstantsvv 0
.vv0 1
Loggingvv1 8
.vv8 9
SHUTDOWN_MESSAGEvv9 I
)vvI J
;vvJ K
awaitww 
Logww 
.ww 
CloseAndFlushAsyncww (
(ww( )
)ww) *
;ww* +
}xx 	
}yy 
private{{ 
static{{ 
IConfiguration{{ !
BuildConfiguration{{" 4
({{4 5
){{5 6
{|| 
string}} 
?}} 
environment}} 
=}} 
Env}} !
.}}! "
	GetString}}" +
(}}+ , 
ApplicationConstants}}, @
.}}@ A
ApplicationSettings}}A T
.}}T U#
DOT_NET_ENVIRONMENT_KEY}}U l
)}}l m
;}}m n
environment 
??=  
ApplicationConstants ,
., -
ApplicationSettings- @
.@ A#
DEVELOPMENT_ENVIRONMENTA X
;X Y
return
ÑÑ 
new
ÑÑ "
ConfigurationBuilder
ÑÑ '
(
ÑÑ' (
)
ÑÑ( )
.
ÖÖ 
SetBasePath
ÖÖ 
(
ÖÖ 

AppContext
ÖÖ #
.
ÖÖ# $
BaseDirectory
ÖÖ$ 1
)
ÖÖ1 2
.
ÜÜ 
AddJsonFile
ÜÜ 
(
ÜÜ "
ApplicationConstants
ÜÜ -
.
ÜÜ- .
Configuration
ÜÜ. ;
.
ÜÜ; <
APP_SETTINGS_FILE
ÜÜ< M
,
ÜÜM N
optional
ÜÜO W
:
ÜÜW X
false
ÜÜY ^
,
ÜÜ^ _
reloadOnChange
ÜÜ` n
:
ÜÜn o
true
ÜÜp t
)
ÜÜt u
.
áá 
AddJsonFile
áá 
(
áá 
string
áá 
.
áá  
Format
áá  &
(
áá& '"
ApplicationConstants
áá' ;
.
áá; <
Configuration
áá< I
.
ááI J.
 ENVIRONMENT_APP_SETTINGS_PATTERN
ááJ j
,
ááj k
environment
áál w
)
ááw x
,
ááx y
optional
àà 
:
àà 
true
àà 
,
àà 
reloadOnChange
àà  .
:
àà. /
true
àà0 4
)
àà4 5
.
ââ %
AddEnvironmentVariables
ââ $
(
ââ$ %
)
ââ% &
.
ää 
Build
ää 
(
ää 
)
ää 
;
ää 
}
ãã 
private
çç 
static
çç 
Logger
çç 
ConfigureSerilog
çç *
(
çç* +
IConfiguration
çç+ 9
configuration
çç: G
)
ççG H
{
éé 
try
èè 
{
êê 	!
LoggerConfiguration
ëë 
loggerConfig
ëë  ,
=
ëë- .
new
ëë/ 2
(
ëë2 3
)
ëë3 4
;
ëë4 5#
IConfigurationSection
ìì !
serilogSection
ìì" 0
=
ìì1 2
configuration
îî 
.
îî 

GetSection
îî (
(
îî( )"
ApplicationConstants
îî) =
.
îî= >
Configuration
îî> K
.
îîK L
SERILOG_SECTION
îîL [
)
îî[ \
;
îî\ ]
string
ññ 
minLevel
ññ 
=
ññ 
serilogSection
ññ ,
[
ññ, -"
ApplicationConstants
ññ- A
.
ññA B
Configuration
ññB O
.
ññO P'
MINIMUM_LEVEL_DEFAULT_KEY
ññP i
]
ññi j
??
ññk m"
ApplicationConstants
óó 2
.
óó2 3
	LogLevels
óó3 <
.
óó< =
INFORMATION
óó= H
;
óóH I
loggerConfig
òò 
=
òò 
minLevel
òò #
switch
òò$ *
{
ôô "
ApplicationConstants
öö $
.
öö$ %
	LogLevels
öö% .
.
öö. /
DEBUG
öö/ 4
=>
öö5 7
loggerConfig
öö8 D
.
ööD E
MinimumLevel
ööE Q
.
ööQ R
Debug
ööR W
(
ööW X
)
ööX Y
,
ööY Z"
ApplicationConstants
õõ $
.
õõ$ %
	LogLevels
õõ% .
.
õõ. /
INFORMATION
õõ/ :
=>
õõ; =
loggerConfig
õõ> J
.
õõJ K
MinimumLevel
õõK W
.
õõW X
Information
õõX c
(
õõc d
)
õõd e
,
õõe f"
ApplicationConstants
úú $
.
úú$ %
	LogLevels
úú% .
.
úú. /
WARNING
úú/ 6
=>
úú7 9
loggerConfig
úú: F
.
úúF G
MinimumLevel
úúG S
.
úúS T
Warning
úúT [
(
úú[ \
)
úú\ ]
,
úú] ^"
ApplicationConstants
ùù $
.
ùù$ %
	LogLevels
ùù% .
.
ùù. /
ERROR
ùù/ 4
=>
ùù5 7
loggerConfig
ùù8 D
.
ùùD E
MinimumLevel
ùùE Q
.
ùùQ R
Error
ùùR W
(
ùùW X
)
ùùX Y
,
ùùY Z"
ApplicationConstants
ûû $
.
ûû$ %
	LogLevels
ûû% .
.
ûû. /
FATAL
ûû/ 4
=>
ûû5 7
loggerConfig
ûû8 D
.
ûûD E
MinimumLevel
ûûE Q
.
ûûQ R
Fatal
ûûR W
(
ûûW X
)
ûûX Y
,
ûûY Z
_
üü 
=>
üü 
loggerConfig
üü !
.
üü! "
MinimumLevel
üü" .
.
üü. /
Information
üü/ :
(
üü: ;
)
üü; <
}
†† 
;
†† 
loggerConfig
¢¢ 
=
¢¢ 
loggerConfig
¢¢ '
.
¢¢' (
WriteTo
¢¢( /
.
¢¢/ 0
Console
¢¢0 7
(
¢¢7 8
)
¢¢8 9
;
¢¢9 :
string
§§ 
logPath
§§ 
=
§§ 
Path
§§ !
.
§§! "
Combine
§§" )
(
§§) *"
ApplicationConstants
§§* >
.
§§> ?
Storage
§§? F
.
§§F G
LOGS_DIRECTORY
§§G U
,
§§U V"
ApplicationConstants
•• $
.
••$ %
Storage
••% ,
.
••, -
LOG_FILE_PATTERN
••- =
)
••= >
;
••> ?
loggerConfig
¶¶ 
=
¶¶ 
loggerConfig
¶¶ '
.
¶¶' (
WriteTo
¶¶( /
.
¶¶/ 0
File
¶¶0 4
(
¶¶4 5
logPath
¶¶5 <
,
¶¶< =
rollingInterval
¶¶> M
:
¶¶M N
RollingInterval
¶¶O ^
.
¶¶^ _
Day
¶¶_ b
)
¶¶b c
;
¶¶c d
return
®® 
loggerConfig
®® 
.
®®  
CreateLogger
®®  ,
(
®®, -
)
®®- .
;
®®. /
}
©© 	
catch
™™ 
(
™™ 
	Exception
™™ 
)
™™ 
{
´´ 	
return
¨¨ 
new
¨¨ !
LoggerConfiguration
¨¨ *
(
¨¨* +
)
¨¨+ ,
.
≠≠ 
MinimumLevel
≠≠ 
.
≠≠ 
Information
≠≠ )
(
≠≠) *
)
≠≠* +
.
ÆÆ 
WriteTo
ÆÆ 
.
ÆÆ 
File
ÆÆ 
(
ÆÆ 
Path
ØØ 
.
ØØ 
Combine
ØØ  
(
ØØ  !"
ApplicationConstants
ØØ! 5
.
ØØ5 6
Storage
ØØ6 =
.
ØØ= >
LOGS_DIRECTORY
ØØ> L
,
ØØL M"
ApplicationConstants
∞∞ ,
.
∞∞, -
Storage
∞∞- 4
.
∞∞4 5
LOG_FILE_PATTERN
∞∞5 E
)
∞∞E F
,
∞∞F G
rollingInterval
∞∞H W
:
∞∞W X
RollingInterval
∞∞Y h
.
∞∞h i
Day
∞∞i l
)
∞∞l m
.
±± 
CreateLogger
±± 
(
±± 
)
±± 
;
±±  
}
≤≤ 	
}
≥≥ 
private
µµ 
static
µµ  
IServiceCollection
µµ %
ConfigureServices
µµ& 7
(
µµ7 8
IConfiguration
µµ8 F
configuration
µµG T
)
µµT U
{
∂∂ 
ServiceCollection
∑∑ 
services
∑∑ "
=
∑∑# $
new
∑∑% (
(
∑∑( )
)
∑∑) *
;
∑∑* +#
ConfigureCoreServices
ππ 
(
ππ 
services
ππ &
,
ππ& '
configuration
ππ( 5
)
ππ5 6
;
ππ6 7&
ConfigureNetworkServices
∫∫  
(
∫∫  !
services
∫∫! )
)
∫∫) *
;
∫∫* +'
ConfigureSecurityServices
ªª !
(
ªª! "
services
ªª" *
,
ªª* +
configuration
ªª, 9
)
ªª9 :
;
ªª: ;(
ConfigureMessagingServices
ºº "
(
ºº" #
services
ºº# +
)
ºº+ ,
;
ºº, --
ConfigureAuthenticationServices
ΩΩ '
(
ΩΩ' (
services
ΩΩ( 0
)
ΩΩ0 1
;
ΩΩ1 2
ConfigureGrpc
ææ 
(
ææ 
services
ææ 
)
ææ 
;
ææ  
ConfigureModules
øø 
(
øø 
services
øø !
)
øø! "
;
øø" #
return
¡¡ 
services
¡¡ 
;
¡¡ 
}
¬¬ 
private
ƒƒ 
static
ƒƒ 
string
ƒƒ 
GetSectionValue
ƒƒ )
(
ƒƒ) *#
IConfigurationSection
ƒƒ* ?
section
ƒƒ@ G
,
ƒƒG H
string
ƒƒI O
key
ƒƒP S
,
ƒƒS T
string
ƒƒU [
defaultValue
ƒƒ\ h
=
ƒƒi j
$str
ƒƒk m
)
ƒƒm n
{
≈≈ 
return
∆∆ 
section
∆∆ 
[
∆∆ 
key
∆∆ 
]
∆∆ 
??
∆∆ 
defaultValue
∆∆ +
;
∆∆+ ,
}
«« 
private
…… 
static
…… 
void
…… #
ConfigureCoreServices
…… -
(
……- . 
IServiceCollection
……. @
services
……A I
,
……I J
IConfiguration
……K Y
configuration
……Z g
)
……g h
{
   
services
ÀÀ 
.
ÀÀ 

AddLogging
ÀÀ 
(
ÀÀ 
builder
ÀÀ #
=>
ÀÀ$ &
builder
ÀÀ' .
.
ÀÀ. /

AddSerilog
ÀÀ/ 9
(
ÀÀ9 :
dispose
ÀÀ: A
:
ÀÀA B
true
ÀÀC G
)
ÀÀG H
)
ÀÀH I
;
ÀÀI J
services
ÕÕ 
.
ŒŒ 
AddDataProtection
ŒŒ 
(
ŒŒ 
)
ŒŒ  
.
œœ  
SetApplicationName
œœ 
(
œœ  "
ApplicationConstants
œœ  4
.
œœ4 5!
ApplicationSettings
œœ5 H
.
œœH I
APPLICATION_NAME
œœI Y
)
œœY Z
.
–– %
PersistKeysToFileSystem
–– $
(
––$ %
new
—— 
DirectoryInfo
—— !
(
——! "
ResolvePath
——" -
(
——- ."
ApplicationConstants
——. B
.
——B C
Storage
——C J
.
——J K'
DATA_PROTECTION_KEYS_PATH
——K d
)
——d e
)
——e f
)
““ 
.
”” #
SetDefaultKeyLifetime
”” "
(
””" #"
ApplicationConstants
””# 7
.
””7 8
Timeouts
””8 @
.
””@ A 
DefaultKeyLifetime
””A S
)
””S T
;
””T U
services
’’ 
.
’’ 
AddSingleton
’’ 
(
’’ 
configuration
’’ +
)
’’+ ,
;
’’, -
services
÷÷ 
.
÷÷ 
AddSingleton
÷÷ 
<
÷÷ 

IScheduler
÷÷ (
>
÷÷( )
(
÷÷) *
AvaloniaScheduler
÷÷* ;
.
÷÷; <
Instance
÷÷< D
)
÷÷D E
;
÷÷E F
}
◊◊ 
private
ŸŸ 
static
ŸŸ 
void
ŸŸ &
ConfigureNetworkServices
ŸŸ 0
(
ŸŸ0 1 
IServiceCollection
ŸŸ1 C
services
ŸŸD L
)
ŸŸL M
{
⁄⁄ 
services
€€ 
.
€€ 
AddHttpClient
€€ 
(
€€ *
InternetConnectivityObserver
€€ ;
.
€€; <
HTTP_CLIENT_NAME
€€< L
,
€€L M
client
€€N T
=>
€€U W
{
‹‹ 	1
#InternetConnectivityObserverOptions
›› /
options
››0 7
=
››8 91
#InternetConnectivityObserverOptions
››: ]
.
››] ^
Default
››^ e
;
››e f
client
ﬁﬁ 
.
ﬁﬁ 
Timeout
ﬁﬁ 
=
ﬁﬁ 
options
ﬁﬁ $
.
ﬁﬁ$ %
ProbeTimeout
ﬁﬁ% 1
;
ﬁﬁ1 2
}
ﬂﬂ 	
)
ﬂﬂ	 

;
ﬂﬂ
 
services
·· 
.
·· 
AddHttpClient
·· 
<
·· #
IIpGeolocationService
·· 4
,
··4 5"
IpGeolocationService
··6 J
>
··J K
(
··K L
)
··L M
.
‚‚  
SetHandlerLifetime
‚‚ 
(
‚‚  "
ApplicationConstants
‚‚  4
.
‚‚4 5
Timeouts
‚‚5 =
.
‚‚= > 
HttpClientLifetime
‚‚> P
)
‚‚P Q
.
„„ 
AddPolicyHandler
„„ 
(
„„ "
HttpPolicyExtensions
„„ 2
.
‰‰ &
HandleTransientHttpError
‰‰ )
(
‰‰) *
)
‰‰* +
.
ÂÂ 
OrResult
ÂÂ 
(
ÂÂ 
msg
ÂÂ 
=>
ÂÂ  
msg
ÂÂ! $
.
ÂÂ$ %

StatusCode
ÂÂ% /
==
ÂÂ0 2
HttpStatusCode
ÂÂ3 A
.
ÂÂA B
TooManyRequests
ÂÂB Q
)
ÂÂQ R
.
ÊÊ 
WaitAndRetryAsync
ÊÊ "
(
ÊÊ" #

retryCount
ÁÁ 
:
ÁÁ "
ApplicationConstants
ÁÁ  4
.
ÁÁ4 5

Thresholds
ÁÁ5 ?
.
ÁÁ? @
RETRY_ATTEMPTS
ÁÁ@ N
,
ÁÁN O#
sleepDurationProvider
ËË )
:
ËË) *
attempt
ËË+ 2
=>
ËË3 5
TimeSpan
ÈÈ  
.
ÈÈ  !
FromSeconds
ÈÈ! ,
(
ÈÈ, -
Math
ÈÈ- 1
.
ÈÈ1 2
Pow
ÈÈ2 5
(
ÈÈ5 6"
ApplicationConstants
ÈÈ6 J
.
ÈÈJ K

Thresholds
ÈÈK U
.
ÈÈU V&
EXPONENTIAL_BACKOFF_BASE
ÈÈV n
,
ÈÈn o
attempt
ÍÍ #
)
ÍÍ# $
)
ÍÍ$ %
)
ÍÍ% &
)
ÍÍ& '
.
ÎÎ 
AddPolicyHandler
ÎÎ 
(
ÎÎ 
Policy
ÎÎ $
.
ÎÎ$ %
TimeoutAsync
ÎÎ% 1
<
ÎÎ1 2!
HttpResponseMessage
ÎÎ2 E
>
ÎÎE F
(
ÎÎF G"
ApplicationConstants
ÎÎG [
.
ÎÎ[ \
Timeouts
ÎÎ\ d
.
ÎÎd e
HttpTimeout
ÎÎe p
)
ÎÎp q
)
ÎÎq r
;
ÎÎr s
services
ÌÌ 
.
ÌÌ 
AddSingleton
ÌÌ 
<
ÌÌ +
IInternetConnectivityObserver
ÌÌ ;
,
ÌÌ; <*
InternetConnectivityObserver
ÌÌ= Y
>
ÌÌY Z
(
ÌÌZ [
)
ÌÌ[ \
;
ÌÌ\ ]
services
ÓÓ 
.
ÓÓ 
AddSingleton
ÓÓ 
(
ÓÓ 
new
ÓÓ !1
#InternetConnectivityObserverOptions
ÓÓ" E
{
ÔÔ 	
PollingInterval
 
=
 "
ApplicationConstants
 2
.
2 3
Timeouts
3 ;
.
; <$
DefaultPollingInterval
< R
,
R S
FailureThreshold
ÒÒ 
=
ÒÒ "
ApplicationConstants
ÒÒ 3
.
ÒÒ3 4

Thresholds
ÒÒ4 >
.
ÒÒ> ?'
DEFAULT_FAILURE_THRESHOLD
ÒÒ? X
,
ÒÒX Y
SuccessThreshold
ÚÚ 
=
ÚÚ "
ApplicationConstants
ÚÚ 3
.
ÚÚ3 4

Thresholds
ÚÚ4 >
.
ÚÚ> ?'
DEFAULT_SUCCESS_THRESHOLD
ÚÚ? X
}
ÛÛ 	
)
ÛÛ	 

;
ÛÛ
 
services
ıı 
.
ıı 
AddSingleton
ıı 
<
ıı  
IRsaChunkEncryptor
ıı 0
,
ıı0 1
RsaChunkEncryptor
ıı2 C
>
ııC D
(
ııD E
)
ııE F
;
ııF G
services
ˆˆ 
.
ˆˆ 
AddSingleton
ˆˆ 
<
ˆˆ $
IPendingRequestManager
ˆˆ 4
,
ˆˆ4 5#
PendingRequestManager
ˆˆ6 K
>
ˆˆK L
(
ˆˆL M
)
ˆˆM N
;
ˆˆN O
services
¯¯ 
.
¯¯ 
AddSingleton
¯¯ 
<
¯¯ )
NetworkProviderDependencies
¯¯ 9
>
¯¯9 :
(
¯¯: ;
sp
¯¯; =
=>
¯¯> @
new
¯¯A D)
NetworkProviderDependencies
¯¯E `
(
¯¯` a
sp
˘˘ 
.
˘˘  
GetRequiredService
˘˘ !
<
˘˘! " 
IRpcServiceManager
˘˘" 4
>
˘˘4 5
(
˘˘5 6
)
˘˘6 7
,
˘˘7 8
sp
˙˙ 
.
˙˙  
GetRequiredService
˙˙ !
<
˙˙! "/
!IApplicationSecureStorageProvider
˙˙" C
>
˙˙C D
(
˙˙D E
)
˙˙E F
,
˙˙F G
sp
˚˚ 
.
˚˚  
GetRequiredService
˚˚ !
<
˚˚! ")
ISecureProtocolStateStorage
˚˚" =
>
˚˚= >
(
˚˚> ?
)
˚˚? @
,
˚˚@ A
sp
¸¸ 
.
¸¸  
GetRequiredService
¸¸ !
<
¸¸! ""
IRpcMetaDataProvider
¸¸" 6
>
¸¸6 7
(
¸¸7 8
)
¸¸8 9
)
¸¸9 :
)
¸¸: ;
;
¸¸; <
services
˛˛ 
.
˛˛ 
AddSingleton
˛˛ 
<
˛˛ %
NetworkProviderServices
˛˛ 5
>
˛˛5 6
(
˛˛6 7
sp
˛˛7 9
=>
˛˛: <
new
˛˛= @%
NetworkProviderServices
˛˛A X
(
˛˛X Y
sp
ˇˇ 
.
ˇˇ  
GetRequiredService
ˇˇ !
<
ˇˇ! ""
IConnectivityService
ˇˇ" 6
>
ˇˇ6 7
(
ˇˇ7 8
)
ˇˇ8 9
,
ˇˇ9 :
sp
ÄÄ 
.
ÄÄ  
GetRequiredService
ÄÄ !
<
ÄÄ! "
IRetryStrategy
ÄÄ" 0
>
ÄÄ0 1
(
ÄÄ1 2
)
ÄÄ2 3
,
ÄÄ3 4
sp
ÅÅ 
.
ÅÅ  
GetRequiredService
ÅÅ !
<
ÅÅ! "$
IPendingRequestManager
ÅÅ" 8
>
ÅÅ8 9
(
ÅÅ9 :
)
ÅÅ: ;
)
ÅÅ; <
)
ÅÅ< =
;
ÅÅ= >
services
ÉÉ 
.
ÉÉ 
AddSingleton
ÉÉ 
<
ÉÉ %
NetworkProviderSecurity
ÉÉ 5
>
ÉÉ5 6
(
ÉÉ6 7
sp
ÉÉ7 9
=>
ÉÉ: <
new
ÉÉ= @%
NetworkProviderSecurity
ÉÉA X
(
ÉÉX Y
sp
ÑÑ 
.
ÑÑ  
GetRequiredService
ÑÑ !
<
ÑÑ! "/
!ICertificatePinningServiceFactory
ÑÑ" C
>
ÑÑC D
(
ÑÑD E
)
ÑÑE F
,
ÑÑF G
sp
ÖÖ 
.
ÖÖ  
GetRequiredService
ÖÖ !
<
ÖÖ! " 
IRsaChunkEncryptor
ÖÖ" 4
>
ÖÖ4 5
(
ÖÖ5 6
)
ÖÖ6 7
,
ÖÖ7 8
sp
ÜÜ 
.
ÜÜ  
GetRequiredService
ÜÜ !
<
ÜÜ! ""
IRetryPolicyProvider
ÜÜ" 6
>
ÜÜ6 7
(
ÜÜ7 8
)
ÜÜ8 9
)
ÜÜ9 :
)
ÜÜ: ;
;
ÜÜ; <
services
àà 
.
àà 
AddSingleton
àà 
<
àà 
NetworkProvider
àà -
>
àà- .
(
àà. /
)
àà/ 0
;
àà0 1
services
ââ 
.
ââ 
AddSingleton
ââ 
<
ââ (
InternetConnectivityBridge
ââ 8
>
ââ8 9
(
ââ9 :
)
ââ: ;
;
ââ; <
}
ää 
private
åå 
static
åå 
void
åå '
ConfigureSecurityServices
åå 1
(
åå1 2 
IServiceCollection
åå2 D
services
ååE M
,
ååM N
IConfiguration
ååO ]
configuration
åå^ k
)
ååk l
{
çç 
services
éé 
.
éé 
AddSingleton
éé 
<
éé 
IOptions
éé &
<
éé& '#
DefaultSystemSettings
éé' <
>
éé< =
>
éé= >
(
éé> ?
_
éé? @
=>
ééA C
{
èè 	#
IConfigurationSection
êê !
section
êê" )
=
êê* +
configuration
ëë 
.
ëë 

GetSection
ëë (
(
ëë( )"
ApplicationConstants
ëë) =
.
ëë= >
Configuration
ëë> K
.
ëëK L*
DEFAULT_APP_SETTINGS_SECTION
ëëL h
)
ëëh i
;
ëëi j#
DefaultSystemSettings
íí !
settings
íí" *
=
íí+ ,
new
íí- 0
(
íí0 1
)
íí1 2
{
ìì 
DEFAULT_THEME
îî 
=
îî 
GetSectionValue
îî  /
(
îî/ 0
section
îî0 7
,
îî7 8"
ApplicationConstants
îî9 M
.
îîM N
ConfigurationKeys
îîN _
.
îî_ `
DEFAULT_THEME
îî` m
)
îîm n
,
îîn o
ENVIRONMENT
ïï 
=
ïï 
GetSectionValue
ïï -
(
ïï- .
section
ïï. 5
,
ïï5 6"
ApplicationConstants
ïï7 K
.
ïïK L
ConfigurationKeys
ïïL ]
.
ïï] ^
ENVIRONMENT
ïï^ i
,
ïïi j"
ApplicationConstants
ññ (
.
ññ( )!
ApplicationSettings
ññ) <
.
ññ< =$
PRODUCTION_ENVIRONMENT
ññ= S
)
ññS T
,
ññT U+
DATA_CENTER_CONNECTION_STRING
óó -
=
óó. /
GetSectionValue
óó0 ?
(
óó? @
section
óó@ G
,
óóG H"
ApplicationConstants
òò (
.
òò( )
ConfigurationKeys
òò) :
.
òò: ;+
DATA_CENTER_CONNECTION_STRING
òò; X
)
òòX Y
,
òòY Z
COUNTRY_CODE_API
ôô  
=
ôô! "
GetSectionValue
ôô# 2
(
ôô2 3
section
ôô3 :
,
ôô: ;"
ApplicationConstants
ôô< P
.
ôôP Q
ConfigurationKeys
ôôQ b
.
ôôb c
COUNTRY_CODE_API
ôôc s
)
ôôs t
,
ôôt u
DOMAIN_NAME
öö 
=
öö 
GetSectionValue
öö -
(
öö- .
section
öö. 5
,
öö5 6"
ApplicationConstants
öö7 K
.
ööK L
ConfigurationKeys
ööL ]
.
öö] ^
DOMAIN_NAME
öö^ i
)
ööi j
,
ööj k
CULTURE
õõ 
=
õõ 
GetSectionValue
õõ )
(
õõ) *
section
õõ* 1
,
õõ1 2"
ApplicationConstants
õõ3 G
.
õõG H
ConfigurationKeys
õõH Y
.
õõY Z
CULTURE
õõZ a
)
õõa b
,
õõb c
PrivacyPolicyUrl
úú  
=
úú! "
GetSectionValue
úú# 2
(
úú2 3
section
úú3 :
,
úú: ;
$str
úú< N
)
úúN O
,
úúO P
TermsOfServiceUrl
ùù !
=
ùù" #
GetSectionValue
ùù$ 3
(
ùù3 4
section
ùù4 ;
,
ùù; <
$str
ùù= P
)
ùùP Q
,
ùùQ R

SupportUrl
ûû 
=
ûû 
GetSectionValue
ûû ,
(
ûû, -
section
ûû- 4
,
ûû4 5
$str
ûû6 B
)
ûûB C
}
üü 
;
üü 
return
†† 
Options
†† 
.
†† 
Create
†† !
(
††! "
settings
††" *
)
††* +
;
††+ ,
}
°° 	
)
°°	 

;
°°
 
services
££ 
.
££ 
AddSingleton
££ 
<
££ 
IOptions
££ &
<
££& ' 
SecureStoreOptions
££' 9
>
££9 :
>
££: ;
(
££; <
_
££< =
=>
££> @
{
§§ 	#
IConfigurationSection
•• !
section
••" )
=
••* +
configuration
¶¶ 
.
¶¶ 

GetSection
¶¶ (
(
¶¶( )"
ApplicationConstants
¶¶) =
.
¶¶= >
Configuration
¶¶> K
.
¶¶K L*
SECURE_STORE_OPTIONS_SECTION
¶¶L h
)
¶¶h i
;
¶¶i j 
SecureStoreOptions
ßß 
options
ßß &
=
ßß' (
new
ßß) ,
(
ßß, -
)
ßß- .
{
®® "
ENCRYPTED_STATE_PATH
©© $
=
©©% &
ResolvePath
©©' 2
(
©©2 3
GetSectionValue
™™ #
(
™™# $
section
™™$ +
,
™™+ ,"
ApplicationConstants
™™- A
.
™™A B
ConfigurationKeys
™™B S
.
™™S T"
ENCRYPTED_STATE_PATH
™™T h
,
™™h i"
ApplicationConstants
´´ ,
.
´´, -
Storage
´´- 4
.
´´4 5 
DEFAULT_STATE_PATH
´´5 G
)
´´G H
)
¨¨ 
}
≠≠ 
;
≠≠ 
return
ÆÆ 
Options
ÆÆ 
.
ÆÆ 
Create
ÆÆ !
(
ÆÆ! "
options
ÆÆ" )
)
ÆÆ) *
;
ÆÆ* +
}
ØØ 	
)
ØØ	 

;
ØØ
 
services
±± 
.
±± 
AddSingleton
±± 
(
±± 
sp
±±  
=>
±±! #
sp
±±$ &
.
±±& ' 
GetRequiredService
±±' 9
<
±±9 :
IOptions
±±: B
<
±±B C#
DefaultSystemSettings
±±C X
>
±±X Y
>
±±Y Z
(
±±Z [
)
±±[ \
.
±±\ ]
Value
±±] b
)
±±b c
;
±±c d
services
≥≥ 
.
≥≥ 
AddSingleton
≥≥ 
<
≥≥ 
ILogger
≥≥ %
<
≥≥% &.
 ApplicationSecureStorageProvider
≥≥& F
>
≥≥F G
>
≥≥G H
(
≥≥H I
sp
≥≥I K
=>
≥≥L N
sp
¥¥ 
.
¥¥  
GetRequiredService
¥¥ !
<
¥¥! "
ILoggerFactory
¥¥" 0
>
¥¥0 1
(
¥¥1 2
)
¥¥2 3
.
¥¥3 4
CreateLogger
¥¥4 @
<
¥¥@ A.
 ApplicationSecureStorageProvider
¥¥A a
>
¥¥a b
(
¥¥b c
)
¥¥c d
)
µµ 	
;
µµ	 

services
∂∂ 
.
∂∂ 
AddSingleton
∂∂ 
<
∂∂ /
!IApplicationSecureStorageProvider
∂∂ ?
,
∂∂? @.
 ApplicationSecureStorageProvider
∂∂A a
>
∂∂a b
(
∂∂b c
)
∂∂c d
;
∂∂d e
services
∏∏ 
.
∏∏ 
AddSingleton
∏∏ 
<
∏∏ '
IPlatformSecurityProvider
∏∏ 7
>
∏∏7 8
(
∏∏8 9
_
∏∏9 :
=>
∏∏; =
{
ππ 	
string
∫∫ 
appDataPath
∫∫ 
=
∫∫  
Path
∫∫! %
.
∫∫% &
Combine
∫∫& -
(
∫∫- .
Environment
ªª 
.
ªª 
GetFolderPath
ªª )
(
ªª) *
Environment
ªª* 5
.
ªª5 6
SpecialFolder
ªª6 C
.
ªªC D"
LocalApplicationData
ªªD X
)
ªªX Y
,
ªªY Z"
ApplicationConstants
ºº $
.
ºº$ %
Storage
ºº% ,
.
ºº, -%
ECLIPTIX_DIRECTORY_NAME
ºº- D
)
ººD E
;
ººE F
return
ΩΩ 
new
ΩΩ +
CrossPlatformSecurityProvider
ΩΩ 4
(
ΩΩ4 5
appDataPath
ΩΩ5 @
)
ΩΩ@ A
;
ΩΩA B
}
ææ 	
)
ææ	 

;
ææ
 
services
¿¿ 
.
¿¿ 
AddSingleton
¿¿ 
<
¿¿ )
ISecureProtocolStateStorage
¿¿ 9
>
¿¿9 :
(
¿¿: ;
sp
¿¿; =
=>
¿¿> @
{
¡¡ 	'
IPlatformSecurityProvider
¬¬ %
platformProvider
¬¬& 6
=
¬¬7 8
sp
¬¬9 ;
.
¬¬; < 
GetRequiredService
¬¬< N
<
¬¬N O'
IPlatformSecurityProvider
¬¬O h
>
¬¬h i
(
¬¬i j
)
¬¬j k
;
¬¬k l
IConfiguration
√√ 
config
√√ !
=
√√" #
sp
√√$ &
.
√√& ' 
GetRequiredService
√√' 9
<
√√9 :
IConfiguration
√√: H
>
√√H I
(
√√I J
)
√√J K
;
√√K L
string
≈≈ 
storageDirectory
≈≈ #
=
≈≈$ %
config
∆∆ 
[
∆∆ "
ApplicationConstants
«« (
.
««( )
Configuration
««) 6
.
««6 7$
SECURE_STORAGE_SECTION
««7 M
+
««N O"
ApplicationConstants
»» (
.
»»( )
Configuration
»») 6
.
»»6 7
PATH_SEPARATOR
»»7 E
+
»»F G"
ApplicationConstants
»»H \
.
»»\ ]
ConfigurationKeys
»»] n
.
»»n o

STATE_PATH
»»o y
]
»»y z
??
…… 
Path
…… 
.
…… 
Combine
…… 
(
……  
Environment
……  +
.
……+ ,
GetFolderPath
……, 9
(
……9 :
Environment
……: E
.
……E F
SpecialFolder
……F S
.
……S T"
LocalApplicationData
……T h
)
……h i
,
……i j"
ApplicationConstants
   (
.
  ( )
Storage
  ) 0
.
  0 1%
ECLIPTIX_DIRECTORY_NAME
  1 H
)
  H I
;
  I J
byte
ÃÃ 
[
ÃÃ 
]
ÃÃ 
deviceId
ÃÃ 
=
ÃÃ 
Encoding
ÃÃ &
.
ÃÃ& '
UTF8
ÃÃ' +
.
ÃÃ+ ,
GetBytes
ÃÃ, 4
(
ÃÃ4 5
Environment
ÃÃ5 @
.
ÃÃ@ A
MachineName
ÃÃA L
+
ÃÃM N
Environment
ÃÃO Z
.
ÃÃZ [
UserName
ÃÃ[ c
)
ÃÃc d
;
ÃÃd e
return
ŒŒ 
new
ŒŒ (
SecureProtocolStateStorage
ŒŒ 1
(
ŒŒ1 2
platformProvider
ŒŒ2 B
,
ŒŒB C
storageDirectory
ŒŒD T
,
ŒŒT U
deviceId
ŒŒV ^
)
ŒŒ^ _
;
ŒŒ_ `
}
œœ 	
)
œœ	 

;
œœ
 
services
—— 
.
—— 
AddSingleton
—— 
<
—— /
!ICertificatePinningServiceFactory
—— ?
,
——? @.
 CertificatePinningServiceFactory
——A a
>
——a b
(
——b c
)
——c d
;
——d e
services
““ 
.
““ 
AddSingleton
““ 
<
““ &
IServerPublicKeyProvider
““ 6
,
““6 7%
ServerPublicKeyProvider
““8 O
>
““O P
(
““P Q
)
““Q R
;
““R S
}
”” 
private
’’ 
static
’’ 
void
’’ (
ConfigureMessagingServices
’’ 2
(
’’2 3 
IServiceCollection
’’3 E
services
’’F N
)
’’N O
{
÷÷ 
services
◊◊ 
.
◊◊ 
AddSingleton
◊◊ 
<
◊◊ 
IMessageBus
◊◊ )
,
◊◊) *

MessageBus
◊◊+ 5
>
◊◊5 6
(
◊◊6 7
)
◊◊7 8
;
◊◊8 9
services
ÿÿ 
.
ÿÿ 
AddSingleton
ÿÿ 
<
ÿÿ "
IConnectivityService
ÿÿ 2
,
ÿÿ2 3!
ConnectivityService
ÿÿ4 G
>
ÿÿG H
(
ÿÿH I
)
ÿÿI J
;
ÿÿJ K
services
ŸŸ 
.
ŸŸ 
AddSingleton
ŸŸ 
<
ŸŸ !
IBottomSheetService
ŸŸ 1
,
ŸŸ1 2 
BottomSheetService
ŸŸ3 E
>
ŸŸE F
(
ŸŸF G
)
ŸŸG H
;
ŸŸH I
services
⁄⁄ 
.
⁄⁄ 
AddSingleton
⁄⁄ 
<
⁄⁄ '
ILanguageDetectionService
⁄⁄ 7
,
⁄⁄7 8&
LanguageDetectionService
⁄⁄9 Q
>
⁄⁄Q R
(
⁄⁄R S
)
⁄⁄S T
;
⁄⁄T U
services
€€ 
.
€€ 
AddSingleton
€€ 
<
€€ "
ILocalizationService
€€ 2
,
€€2 3!
LocalizationService
€€4 G
>
€€G H
(
€€H I
)
€€I J
;
€€J K
services
‹‹ 
.
‹‹ 
AddTransient
‹‹ 
<
‹‹ 
ILogoutService
‹‹ ,
,
‹‹, -
LogoutService
‹‹. ;
>
‹‹; <
(
‹‹< =
)
‹‹= >
;
‹‹> ?
services
ﬁﬁ 
.
ﬁﬁ 
AddSingleton
ﬁﬁ 
<
ﬁﬁ &
IApplicationStateManager
ﬁﬁ 6
,
ﬁﬁ6 7%
ApplicationStateManager
ﬁﬁ8 O
>
ﬁﬁO P
(
ﬁﬁP Q
)
ﬁﬁQ R
;
ﬁﬁR S
services
ﬂﬂ 
.
ﬂﬂ 
AddSingleton
ﬂﬂ 
<
ﬂﬂ "
IStateCleanupService
ﬂﬂ 2
,
ﬂﬂ2 3!
StateCleanupService
ﬂﬂ4 G
>
ﬂﬂG H
(
ﬂﬂH I
)
ﬂﬂI J
;
ﬂﬂJ K
services
‡‡ 
.
‡‡ 
AddSingleton
‡‡ 
<
‡‡  
IApplicationRouter
‡‡ 0
,
‡‡0 1
ApplicationRouter
‡‡2 C
>
‡‡C D
(
‡‡D E
)
‡‡E F
;
‡‡F G
services
·· 
.
·· 
AddTransient
·· 
<
··  
ApplicationStartup
·· 0
>
··0 1
(
··1 2
)
··2 3
;
··3 4
}
‚‚ 
private
‰‰ 
static
‰‰ 
void
‰‰ -
ConfigureAuthenticationServices
‰‰ 7
(
‰‰7 8 
IServiceCollection
‰‰8 J
services
‰‰K S
)
‰‰S T
{
ÂÂ 
services
ÊÊ 
.
ÊÊ 
AddSingleton
ÊÊ 
<
ÊÊ $
IAuthenticationService
ÊÊ 4
,
ÊÊ4 5)
OpaqueAuthenticationService
ÊÊ6 Q
>
ÊÊQ R
(
ÊÊR S
)
ÊÊS T
;
ÊÊT U
services
ÁÁ 
.
ÁÁ 
AddSingleton
ÁÁ 
<
ÁÁ (
IOpaqueRegistrationService
ÁÁ 8
,
ÁÁ8 9'
OpaqueRegistrationService
ÁÁ: S
>
ÁÁS T
(
ÁÁT U
)
ÁÁU V
;
ÁÁV W
services
ËË 
.
ËË 
AddSingleton
ËË 
<
ËË '
ISecureKeyRecoveryService
ËË 7
,
ËË7 8&
SecureKeyRecoveryService
ËË9 Q
>
ËËQ R
(
ËËR S
)
ËËS T
;
ËËT U
services
ÈÈ 
.
ÈÈ 
AddSingleton
ÈÈ 
<
ÈÈ 
IIdentityService
ÈÈ .
,
ÈÈ. /
IdentityService
ÈÈ0 ?
>
ÈÈ? @
(
ÈÈ@ A
)
ÈÈA B
;
ÈÈB C
services
ÎÎ 
.
ÎÎ 
AddSingleton
ÎÎ 
<
ÎÎ $
IHardenedKeyDerivation
ÎÎ 4
,
ÎÎ4 5#
HardenedKeyDerivation
ÎÎ6 K
>
ÎÎK L
(
ÎÎL M
)
ÎÎM N
;
ÎÎN O
services
ÌÌ 
.
ÌÌ 
AddSingleton
ÌÌ 
<
ÌÌ %
IApplicationInitializer
ÌÌ 5
,
ÌÌ5 6$
ApplicationInitializer
ÌÌ7 M
>
ÌÌM N
(
ÌÌN O
)
ÌÌO P
;
ÌÌP Q
services
ÓÓ 
.
ÓÓ 
AddSingleton
ÓÓ 
<
ÓÓ  
IRpcServiceManager
ÓÓ 0
,
ÓÓ0 1
RpcServiceManager
ÓÓ2 C
>
ÓÓC D
(
ÓÓD E
)
ÓÓE F
;
ÓÓF G
services
 
.
 
AddSingleton
 
<
 (
RetryStrategyConfiguration
 8
>
8 9
(
9 :
sp
: <
=>
= ?
{
ÒÒ 	
IConfiguration
ÚÚ 
config
ÚÚ !
=
ÚÚ" #
sp
ÚÚ$ &
.
ÚÚ& ' 
GetRequiredService
ÚÚ' 9
<
ÚÚ9 :
IConfiguration
ÚÚ: H
>
ÚÚH I
(
ÚÚI J
)
ÚÚJ K
;
ÚÚK L#
IConfigurationSection
ÛÛ !
section
ÛÛ" )
=
ÛÛ* +
config
ÙÙ 
.
ÙÙ 

GetSection
ÙÙ !
(
ÙÙ! ""
ApplicationConstants
ÙÙ" 6
.
ÙÙ6 7
Configuration
ÙÙ7 D
.
ÙÙD E2
$SECRECY_CHANNEL_RETRY_POLICY_SECTION
ÙÙE i
)
ÙÙi j
;
ÙÙj k
return
ıı &
CreateRetryConfiguration
ıı +
(
ıı+ ,
section
ıı, 3
)
ıı3 4
;
ıı4 5
}
ˆˆ 	
)
ˆˆ	 

;
ˆˆ
 
services
¯¯ 
.
¯¯ 
AddSingleton
¯¯ 
<
¯¯ '
IOperationTimeoutProvider
¯¯ 7
,
¯¯7 8&
OperationTimeoutProvider
¯¯9 Q
>
¯¯Q R
(
¯¯R S
)
¯¯S T
;
¯¯T U
services
˙˙ 
.
˙˙ 
AddSingleton
˙˙ 
<
˙˙ "
IRetryPolicyProvider
˙˙ 2
>
˙˙2 3
(
˙˙3 4
sp
˙˙4 6
=>
˙˙7 9
{
˚˚ 	(
RetryStrategyConfiguration
¸¸ &!
retryStrategyConfig
¸¸' :
=
¸¸; <
sp
¸¸= ?
.
¸¸? @ 
GetRequiredService
¸¸@ R
<
¸¸R S(
RetryStrategyConfiguration
¸¸S m
>
¸¸m n
(
¸¸n o
)
¸¸o p
;
¸¸p q
return
˝˝ 
new
˝˝ !
RetryPolicyProvider
˝˝ *
(
˝˝* +!
retryStrategyConfig
˝˝+ >
)
˝˝> ?
;
˝˝? @
}
˛˛ 	
)
˛˛	 

;
˛˛
 
services
ÄÄ 
.
ÄÄ 
AddSingleton
ÄÄ 
<
ÄÄ 
IRetryStrategy
ÄÄ ,
>
ÄÄ, -
(
ÄÄ- .
sp
ÄÄ. 0
=>
ÄÄ1 3
{
ÅÅ 	(
RetryStrategyConfiguration
ÇÇ &!
retryStrategyConfig
ÇÇ' :
=
ÇÇ; <
sp
ÇÇ= ?
.
ÇÇ? @ 
GetRequiredService
ÇÇ@ R
<
ÇÇR S(
RetryStrategyConfiguration
ÇÇS m
>
ÇÇm n
(
ÇÇn o
)
ÇÇo p
;
ÇÇp q"
IConnectivityService
ÉÉ  !
connectivityService
ÉÉ! 4
=
ÉÉ5 6
sp
ÉÉ7 9
.
ÉÉ9 : 
GetRequiredService
ÉÉ: L
<
ÉÉL M"
IConnectivityService
ÉÉM a
>
ÉÉa b
(
ÉÉb c
)
ÉÉc d
;
ÉÉd e'
IOperationTimeoutProvider
ÑÑ %
timeoutProvider
ÑÑ& 5
=
ÑÑ6 7
sp
ÑÑ8 :
.
ÑÑ: ; 
GetRequiredService
ÑÑ; M
<
ÑÑM N'
IOperationTimeoutProvider
ÑÑN g
>
ÑÑg h
(
ÑÑh i
)
ÑÑi j
;
ÑÑj k
RetryStrategy
ÜÜ 
retryStrategy
ÜÜ '
=
ÜÜ( )
new
ÜÜ* -
(
ÜÜ- .!
retryStrategyConfig
ÜÜ. A
,
ÜÜA B!
connectivityService
ÜÜC V
,
ÜÜV W
timeoutProvider
ÜÜX g
)
ÜÜg h
;
ÜÜh i
Lazy
áá 
<
áá 
NetworkProvider
áá  
>
áá  !
lazyProvider
áá" .
=
áá/ 0
new
áá1 4
(
áá4 5
sp
áá5 7
.
áá7 8 
GetRequiredService
áá8 J
<
ááJ K
NetworkProvider
ááK Z
>
ááZ [
)
áá[ \
;
áá\ ]
retryStrategy
àà 
.
àà $
SetLazyNetworkProvider
àà 0
(
àà0 1
lazyProvider
àà1 =
)
àà= >
;
àà> ?
return
ââ 
retryStrategy
ââ  
;
ââ  !
}
ää 	
)
ää	 

;
ää
 
services
åå 
.
åå 
AddSingleton
åå 
<
åå !
IGrpcErrorProcessor
åå 1
,
åå1 2 
GrpcErrorProcessor
åå3 E
>
ååE F
(
ååF G
)
ååG H
;
ååH I
services
çç 
.
çç 
AddSingleton
çç 
<
çç #
IGrpcDeadlineProvider
çç 3
,
çç3 4"
GrpcDeadlineProvider
çç5 I
>
ççI J
(
ççJ K
)
ççK L
;
ççL M
services
éé 
.
éé 
AddSingleton
éé 
<
éé %
IGrpcCallOptionsFactory
éé 5
,
éé5 6$
GrpcCallOptionsFactory
éé7 M
>
ééM N
(
ééN O
)
ééO P
;
ééP Q
services
èè 
.
èè 
AddSingleton
èè 
<
èè 
IUnaryRpcServices
èè /
,
èè/ 0
UnaryRpcServices
èè1 A
>
èèA B
(
èèB C
)
èèC D
;
èèD E
services
êê 
.
êê 
AddSingleton
êê 
<
êê (
ISecrecyChannelRpcServices
êê 8
,
êê8 9'
SecrecyChannelRpcServices
êê: S
>
êêS T
(
êêT U
)
êêU V
;
êêV W
services
ëë 
.
ëë 
AddSingleton
ëë 
<
ëë '
IReceiveStreamRpcServices
ëë 7
,
ëë7 8&
ReceiveStreamRpcServices
ëë9 Q
>
ëëQ R
(
ëëR S
)
ëëS T
;
ëëT U
services
íí 
.
íí 
AddSingleton
íí 
<
íí "
IRpcMetaDataProvider
íí 2
,
íí2 3!
RpcMetaDataProvider
íí4 G
>
ííG H
(
ííH I
)
ííI J
;
ííJ K
services
ìì 
.
ìì 
AddSingleton
ìì 
<
ìì (
RequestMetaDataInterceptor
ìì 8
>
ìì8 9
(
ìì9 :
)
ìì: ;
;
ìì; <
}
îî 
private
ññ 
static
ññ (
RetryStrategyConfiguration
ññ -&
CreateRetryConfiguration
ññ. F
(
ññF G#
IConfigurationSection
ññG \
section
ññ] d
)
ññd e
{
óó 
return
òò 
new
òò (
RetryStrategyConfiguration
òò -
{
ôô 	!
INITIAL_RETRY_DELAY
öö 
=
öö  !
TimeSpan
öö" *
.
öö* +
TryParse
öö+ 3
(
öö3 4
section
öö4 ;
[
öö; <"
ApplicationConstants
öö< P
.
ööP Q
ConfigurationKeys
ööQ b
.
ööb c!
INITIAL_RETRY_DELAY
ööc v
]
ööv w
,
ööw x
out
õõ 
TimeSpan
õõ 
initialDelay
õõ )
)
õõ) *
?
úú 
initialDelay
úú 
:
ùù "
ApplicationConstants
ùù &
.
ùù& '
Timeouts
ùù' /
.
ùù/ 0&
DefaultInitialRetryDelay
ùù0 H
,
ùùH I
MAX_RETRY_DELAY
ûû 
=
ûû 
TimeSpan
ûû &
.
ûû& '
TryParse
ûû' /
(
ûû/ 0
section
ûû0 7
[
ûû7 8"
ApplicationConstants
ûû8 L
.
ûûL M
ConfigurationKeys
ûûM ^
.
ûû^ _
MAX_RETRY_DELAY
ûû_ n
]
ûûn o
,
ûûo p
out
üü 
TimeSpan
üü 
maxDelay
üü %
)
üü% &
?
†† 
maxDelay
†† 
:
°° "
ApplicationConstants
°° &
.
°°& '
Timeouts
°°' /
.
°°/ 0"
DefaultMaxRetryDelay
°°0 D
,
°°D E
MAX_RETRIES
¢¢ 
=
¢¢ 
int
¢¢ 
.
¢¢ 
TryParse
¢¢ &
(
¢¢& '
section
¢¢' .
[
¢¢. /"
ApplicationConstants
¢¢/ C
.
¢¢C D
ConfigurationKeys
¢¢D U
.
¢¢U V
MAX_RETRIES
¢¢V a
]
¢¢a b
,
¢¢b c
out
¢¢d g
int
¢¢h k

maxRetries
¢¢l v
)
¢¢v w
?
££ 

maxRetries
££ 
:
§§ "
ApplicationConstants
§§ &
.
§§& '

Thresholds
§§' 1
.
§§1 2!
DEFAULT_MAX_RETRIES
§§2 E
,
§§E F!
PER_ATTEMPT_TIMEOUT
•• 
=
••  !
TimeSpan
••" *
.
••* +
TryParse
••+ 3
(
••3 4
section
••4 ;
[
••; <"
ApplicationConstants
••< P
.
••P Q
ConfigurationKeys
••Q b
.
••b c!
PER_ATTEMPT_TIMEOUT
••c v
]
••v w
,
••w x
out
¶¶ 
TimeSpan
¶¶ 
perAttemptTimeout
¶¶ .
)
¶¶. /
?
ßß 
perAttemptTimeout
ßß #
:
®® 
TimeSpan
®® 
.
®® 
FromSeconds
®® &
(
®®& '
$num
®®' )
)
®®) *
,
®®* + 
USE_ADAPTIVE_RETRY
©© 
=
©©  
!
™™ 
bool
™™ 
.
™™ 
TryParse
™™ 
(
™™ 
section
™™ &
[
™™& '"
ApplicationConstants
™™' ;
.
™™; <
ConfigurationKeys
™™< M
.
™™M N 
USE_ADAPTIVE_RETRY
™™N `
]
™™` a
,
™™a b
out
™™c f
bool
™™g k
adaptive
™™l t
)
™™t u
||
™™v x
adaptive
´´ 
}
¨¨ 	
;
¨¨	 

}
≠≠ 
private
ØØ 
static
ØØ 
void
ØØ 
ConfigureGrpc
ØØ %
(
ØØ% & 
IServiceCollection
ØØ& 8
services
ØØ9 A
)
ØØA B
{
∞∞ 
services
±± 
.
±± 
AddSingleton
±± 
(
±± 
(
±± 
Action
±± %
<
±±% &&
GrpcClientFactoryOptions
±±& >
>
±±> ?
)
±±? @$
ConfigureClientOptions
±±@ V
)
±±V W
;
±±W X
services
≤≤ 
.
≤≤ &
AddConfiguredGrpcClients
≤≤ )
(
≤≤) *
)
≤≤* +
;
≤≤+ ,
return
≥≥ 
;
≥≥ 
void
µµ $
ConfigureClientOptions
µµ #
(
µµ# $&
GrpcClientFactoryOptions
µµ$ <
options
µµ= D
)
µµD E
{
∂∂ 	#
DefaultSystemSettings
∑∑ !
settings
∑∑" *
=
∑∑+ ,
services
∑∑- 5
.
∑∑5 6"
BuildServiceProvider
∑∑6 J
(
∑∑J K
)
∑∑K L
.
∏∏  
GetRequiredService
∏∏ #
<
∏∏# $#
DefaultSystemSettings
∏∏$ 9
>
∏∏9 :
(
∏∏: ;
)
∏∏; <
;
∏∏< =
string
ππ 
endpoint
ππ 
=
ππ 
settings
∫∫ 
.
∫∫ 
ENVIRONMENT
∫∫ $
.
∫∫$ %
Equals
∫∫% +
(
∫∫+ ,"
ApplicationConstants
∫∫, @
.
∫∫@ A!
ApplicationSettings
∫∫A T
.
∫∫T U%
DEVELOPMENT_ENVIRONMENT
∫∫U l
,
∫∫l m
StringComparison
ªª $
.
ªª$ %
OrdinalIgnoreCase
ªª% 6
)
ªª6 7
?
ºº 
settings
ºº 
.
ºº +
DATA_CENTER_CONNECTION_STRING
ºº <
:
ΩΩ 
string
ΩΩ 
.
ΩΩ 
Empty
ΩΩ "
;
ΩΩ" #
if
øø 
(
øø 
string
øø 
.
øø 
IsNullOrEmpty
øø $
(
øø$ %
endpoint
øø% -
)
øø- .
)
øø. /
{
¿¿ 
throw
¡¡ 
new
¡¡ '
InvalidOperationException
¡¡ 3
(
¡¡3 4"
ApplicationConstants
¡¡4 H
.
¡¡H I
Logging
¡¡I P
.
¡¡P Q)
GRPC_ENDPOINT_ERROR_MESSAGE
¡¡Q l
)
¡¡l m
;
¡¡m n
}
¬¬ 
options
ƒƒ 
.
ƒƒ 
Address
ƒƒ 
=
ƒƒ 
new
ƒƒ !
Uri
ƒƒ" %
(
ƒƒ% &
endpoint
ƒƒ& .
)
ƒƒ. /
;
ƒƒ/ 0
}
≈≈ 	
}
∆∆ 
private
»» 
static
»» 
void
»» 
ConfigureModules
»» (
(
»»( ) 
IServiceCollection
»») ;
services
»»< D
)
»»D E
{
…… 
services
   
.
   
AddSingleton
   
<
   #
ModuleResourceManager
   3
>
  3 4
(
  4 5
)
  5 6
;
  6 7
services
ÃÃ 
.
ÃÃ 
AddSingleton
ÃÃ 
<
ÃÃ 
IModuleMessageBus
ÃÃ /
,
ÃÃ/ 0
ModuleMessageBus
ÃÃ1 A
>
ÃÃA B
(
ÃÃB C
)
ÃÃC D
;
ÃÃD E
services
ÕÕ 
.
ÕÕ 
AddSingleton
ÕÕ 
<
ÕÕ  
IModuleViewFactory
ÕÕ 0
,
ÕÕ0 1
ModuleViewFactory
ÕÕ2 C
>
ÕÕC D
(
ÕÕD E
)
ÕÕE F
;
ÕÕF G
services
œœ 
.
œœ 
AddSingleton
œœ 
<
œœ 
IViewLocator
œœ *
,
œœ* +
ViewLocator
œœ, 7
>
œœ7 8
(
œœ8 9
)
œœ9 :
;
œœ: ;
services
–– 
.
–– 
AddSingleton
–– 
<
–– *
ReactiveUiViewLocatorAdapter
–– :
>
––: ;
(
––; <
)
––< =
;
––= >
services
““ 
.
““ 
AddSingleton
““ 
<
““ 

ReactiveUI
““ (
.
““( )
IViewLocator
““) 5
>
““5 6
(
““6 7
provider
““7 ?
=>
““@ B
provider
”” 
.
””  
GetRequiredService
”” '
<
””' (*
ReactiveUiViewLocatorAdapter
””( D
>
””D E
(
””E F
)
””F G
)
””G H
;
””H I
ModuleCatalog
’’ 
catalog
’’ 
=
’’ 
new
’’  #
(
’’# $
)
’’$ %
;
’’% &
catalog
÷÷ 
.
÷÷ 
	AddModule
÷÷ 
<
÷÷ "
AuthenticationModule
÷÷ .
>
÷÷. /
(
÷÷/ 0
)
÷÷0 1
;
÷÷1 2
catalog
◊◊ 
.
◊◊ 
	AddModule
◊◊ 
<
◊◊ 

MainModule
◊◊ $
>
◊◊$ %
(
◊◊% &
)
◊◊& '
;
◊◊' (
services
ŸŸ 
.
ŸŸ 
AddSingleton
ŸŸ 
<
ŸŸ 
IModuleCatalog
ŸŸ ,
>
ŸŸ, -
(
ŸŸ- .
catalog
ŸŸ. 5
)
ŸŸ5 6
;
ŸŸ6 7
services
⁄⁄ 
.
⁄⁄ 
AddSingleton
⁄⁄ 
(
⁄⁄ 
catalog
⁄⁄ %
)
⁄⁄% &
;
⁄⁄& '
services
‹‹ 
.
‹‹ 
AddSingleton
‹‹ 
<
‹‹ 
IModuleManager
‹‹ ,
,
‹‹, -
ModuleManager
‹‹. ;
>
‹‹; <
(
‹‹< =
)
‹‹= >
;
‹‹> ?
services
ﬁﬁ 
.
ﬁﬁ 
AddTransient
ﬁﬁ 
<
ﬁﬁ '
LanguageSelectorViewModel
ﬁﬁ 7
>
ﬁﬁ7 8
(
ﬁﬁ8 9
)
ﬁﬁ9 :
;
ﬁﬁ: ;
services
ﬂﬂ 
.
ﬂﬂ 
AddSingleton
ﬂﬂ 
<
ﬂﬂ "
BottomSheetViewModel
ﬂﬂ 2
>
ﬂﬂ2 3
(
ﬂﬂ3 4
)
ﬂﬂ4 5
;
ﬂﬂ5 6
services
‡‡ 
.
‡‡ 
AddSingleton
‡‡ 
<
‡‡ /
!ConnectivityNotificationViewModel
‡‡ ?
>
‡‡? @
(
‡‡@ A
)
‡‡A B
;
‡‡B C
services
·· 
.
·· 
AddSingleton
·· 
<
·· 
Ecliptix
·· &
.
··& '
Core
··' +
.
··+ ,

ViewModels
··, 6
.
··6 7
Core
··7 ;
.
··; <!
MainWindowViewModel
··< O
>
··O P
(
··P Q
)
··Q R
;
··R S
services
‚‚ 
.
‚‚ 
AddTransient
‚‚ 
<
‚‚ #
SplashWindowViewModel
‚‚ 3
>
‚‚3 4
(
‚‚4 5
)
‚‚5 6
;
‚‚6 7
services
„„ 
.
„„ 
AddTransient
„„ 
<
„„ %
AuthenticationViewModel
„„ 5
>
„„5 6
(
„„6 7
sp
„„7 9
=>
„„: <
new
„„= @%
AuthenticationViewModel
„„A X
(
„„X Y
new
‰‰ 1
#AuthenticationViewModelDependencies
‰‰ 3
{
ÂÂ !
ConnectivityService
ÊÊ #
=
ÊÊ$ %
sp
ÊÊ& (
.
ÊÊ( ) 
GetRequiredService
ÊÊ) ;
<
ÊÊ; <"
IConnectivityService
ÊÊ< P
>
ÊÊP Q
(
ÊÊQ R
)
ÊÊR S
,
ÊÊS T
NetworkProvider
ÁÁ 
=
ÁÁ  !
sp
ÁÁ" $
.
ÁÁ$ % 
GetRequiredService
ÁÁ% 7
<
ÁÁ7 8
NetworkProvider
ÁÁ8 G
>
ÁÁG H
(
ÁÁH I
)
ÁÁI J
,
ÁÁJ K!
LocalizationService
ËË #
=
ËË$ %
sp
ËË& (
.
ËË( ) 
GetRequiredService
ËË) ;
<
ËË; <"
ILocalizationService
ËË< P
>
ËËP Q
(
ËËQ R
)
ËËR S
,
ËËS T
StorageProvider
ÈÈ 
=
ÈÈ  !
sp
ÈÈ" $
.
ÈÈ$ % 
GetRequiredService
ÈÈ% 7
<
ÈÈ7 8/
!IApplicationSecureStorageProvider
ÈÈ8 Y
>
ÈÈY Z
(
ÈÈZ [
)
ÈÈ[ \
,
ÈÈ\ ]#
AuthenticationService
ÍÍ %
=
ÍÍ& '
sp
ÍÍ( *
.
ÍÍ* + 
GetRequiredService
ÍÍ+ =
<
ÍÍ= >$
IAuthenticationService
ÍÍ> T
>
ÍÍT U
(
ÍÍU V
)
ÍÍV W
,
ÍÍW X!
RegistrationService
ÎÎ #
=
ÎÎ$ %
sp
ÎÎ& (
.
ÎÎ( ) 
GetRequiredService
ÎÎ) ;
<
ÎÎ; <(
IOpaqueRegistrationService
ÎÎ< V
>
ÎÎV W
(
ÎÎW X
)
ÎÎX Y
,
ÎÎY Z
RecoveryService
ÏÏ 
=
ÏÏ  !
sp
ÏÏ" $
.
ÏÏ$ % 
GetRequiredService
ÏÏ% 7
<
ÏÏ7 8'
ISecureKeyRecoveryService
ÏÏ8 Q
>
ÏÏQ R
(
ÏÏR S
)
ÏÏS T
,
ÏÏT U&
LanguageDetectionService
ÌÌ (
=
ÌÌ) *
sp
ÌÌ+ -
.
ÌÌ- . 
GetRequiredService
ÌÌ. @
<
ÌÌ@ A'
ILanguageDetectionService
ÌÌA Z
>
ÌÌZ [
(
ÌÌ[ \
)
ÌÌ\ ]
,
ÌÌ] ^
Router
ÓÓ 
=
ÓÓ 
sp
ÓÓ 
.
ÓÓ  
GetRequiredService
ÓÓ .
<
ÓÓ. / 
IApplicationRouter
ÓÓ/ A
>
ÓÓA B
(
ÓÓB C
)
ÓÓC D
,
ÓÓD E!
MainWindowViewModel
ÔÔ #
=
ÔÔ$ %
sp
ÔÔ& (
.
ÔÔ( ) 
GetRequiredService
ÔÔ) ;
<
ÔÔ; <
Ecliptix
ÔÔ< D
.
ÔÔD E
Core
ÔÔE I
.
ÔÔI J

ViewModels
ÔÔJ T
.
ÔÔT U
Core
ÔÔU Y
.
ÔÔY Z!
MainWindowViewModel
ÔÔZ m
>
ÔÔm n
(
ÔÔn o
)
ÔÔo p
,
ÔÔp q
Settings
 
=
 
sp
 
.
  
GetRequiredService
 0
<
0 1#
DefaultSystemSettings
1 F
>
F G
(
G H
)
H I
}
ÒÒ 
)
ÒÒ 
)
ÒÒ 
;
ÒÒ 
services
ÚÚ 
.
ÚÚ 
AddTransient
ÚÚ 
<
ÚÚ 
MasterViewModel
ÚÚ -
>
ÚÚ- .
(
ÚÚ. /
)
ÚÚ/ 0
;
ÚÚ0 1
}
ÛÛ 
private
ıı 
static
ıı 
string
ıı )
GetPlatformAppDataDirectory
ıı 5
(
ıı5 6
)
ıı6 7
{
ˆˆ 
if
˜˜ 

(
˜˜  
RuntimeInformation
˜˜ 
.
˜˜ 
IsOSPlatform
˜˜ +
(
˜˜+ ,

OSPlatform
˜˜, 6
.
˜˜6 7
Windows
˜˜7 >
)
˜˜> ?
)
˜˜? @
{
¯¯ 	
return
˘˘ 
Environment
˘˘ 
.
˘˘ 
GetFolderPath
˘˘ ,
(
˘˘, -
Environment
˘˘- 8
.
˘˘8 9
SpecialFolder
˘˘9 F
.
˘˘F G
ApplicationData
˘˘G V
)
˘˘V W
;
˘˘W X
}
˙˙ 	
if
¸¸ 

(
¸¸  
RuntimeInformation
¸¸ 
.
¸¸ 
IsOSPlatform
¸¸ +
(
¸¸+ ,

OSPlatform
¸¸, 6
.
¸¸6 7
Linux
¸¸7 <
)
¸¸< =
)
¸¸= >
{
˝˝ 	
return
˛˛ 
Path
˛˛ 
.
˛˛ 
Combine
˛˛ 
(
˛˛  
Environment
ˇˇ 
.
ˇˇ 
GetFolderPath
ˇˇ )
(
ˇˇ) *
Environment
ˇˇ* 5
.
ˇˇ5 6
SpecialFolder
ˇˇ6 C
.
ˇˇC D
UserProfile
ˇˇD O
)
ˇˇO P
,
ˇˇP Q"
ApplicationConstants
ÄÄ $
.
ÄÄ$ %
Storage
ÄÄ% ,
.
ÄÄ, -#
LOCAL_SHARE_DIRECTORY
ÄÄ- B
)
ÅÅ 
;
ÅÅ 
}
ÇÇ 	
return
ÑÑ 
Path
ÑÑ 
.
ÑÑ 
Combine
ÑÑ 
(
ÑÑ 
Environment
ÖÖ 
.
ÖÖ 
GetFolderPath
ÖÖ %
(
ÖÖ% &
Environment
ÖÖ& 1
.
ÖÖ1 2
SpecialFolder
ÖÖ2 ?
.
ÖÖ? @
UserProfile
ÖÖ@ K
)
ÖÖK L
,
ÖÖL M"
ApplicationConstants
ÜÜ  
.
ÜÜ  !
Storage
ÜÜ! (
.
ÜÜ( )+
APPLICATION_SUPPORT_DIRECTORY
ÜÜ) F
)
áá 	
;
áá	 

}
àà 
private
ää 
static
ää 
void
ää (
SetSecurePermissionsIfUnix
ää 2
(
ää2 3
string
ää3 9
	directory
ää: C
)
ääC D
{
ãã 
if
åå 

(
åå 
!
åå  
RuntimeInformation
åå 
.
åå  
IsOSPlatform
åå  ,
(
åå, -

OSPlatform
åå- 7
.
åå7 8
Linux
åå8 =
)
åå= >
&&
åå? A
!
çç  
RuntimeInformation
çç 
.
çç  
IsOSPlatform
çç  ,
(
çç, -

OSPlatform
çç- 7
.
çç7 8
OSX
çç8 ;
)
çç; <
)
çç< =
{
éé 	
return
èè 
;
èè 
}
êê 	
try
íí 
{
ìì 	
File
îî 
.
îî 
SetUnixFileMode
îî  
(
îî  !
	directory
îî! *
,
îî* +"
ApplicationConstants
îî, @
.
îî@ A
FilePermissions
îîA P
.
îîP Q#
SECURE_DIRECTORY_MODE
îîQ f
)
îîf g
;
îîg h
Log
ïï 
.
ïï 
Debug
ïï 
(
ïï "
ApplicationConstants
ïï *
.
ïï* +
Logging
ïï+ 2
.
ïï2 3%
PERMISSIONS_SET_MESSAGE
ïï3 J
,
ïïJ K
	directory
ïïL U
)
ïïU V
;
ïïV W
}
ññ 	
catch
óó 
(
óó 
IOException
óó 
ex
óó 
)
óó 
{
òò 	
Log
ôô 
.
ôô 
Warning
ôô 
(
ôô 
ex
ôô 
,
ôô "
ApplicationConstants
ôô 0
.
ôô0 1
Logging
ôô1 8
.
ôô8 9&
PERMISSIONS_FAIL_MESSAGE
ôô9 Q
,
ôôQ R
	directory
ôôS \
)
ôô\ ]
;
ôô] ^
}
öö 	
}
õõ 
private
ùù 
static
ùù 
string
ùù 
ResolvePath
ùù %
(
ùù% &
string
ùù& ,
path
ùù- 1
)
ùù1 2
{
ûû 
if
üü 

(
üü 
string
üü 
.
üü 
IsNullOrEmpty
üü  
(
üü  !
path
üü! %
)
üü% &
)
üü& '
{
†† 	
throw
°° 
new
°° 
ArgumentException
°° '
(
°°' ("
ApplicationConstants
°°( <
.
°°< =
Logging
°°= D
.
°°D E&
PATH_EMPTY_ERROR_MESSAGE
°°E ]
,
°°] ^
nameof
°°_ e
(
°°e f
path
°°f j
)
°°j k
)
°°k l
;
°°l m
}
¢¢ 	
string
§§ 

appDataDir
§§ 
=
§§ )
GetPlatformAppDataDirectory
§§ 7
(
§§7 8
)
§§8 9
;
§§9 :
path
¶¶ 
=
¶¶ 
Environment
¶¶ 
.
¶¶ (
ExpandEnvironmentVariables
¶¶ 5
(
¶¶5 6
path
ßß 
.
ßß 
Replace
ßß 
(
ßß "
ApplicationConstants
ßß -
.
ßß- .
Storage
ßß. 5
.
ßß5 6+
APP_DATA_ENVIRONMENT_VARIABLE
ßß6 S
,
ßßS T
Path
®® 
.
®® 
Combine
®® 
(
®® 

appDataDir
®® '
,
®®' ("
ApplicationConstants
®®) =
.
®®= >
Storage
®®> E
.
®®E F%
ECLIPTIX_DIRECTORY_NAME
®®F ]
)
®®] ^
)
®®^ _
)
©© 	
;
©©	 

string
´´ 
?
´´ 
	directory
´´ 
=
´´ 
Path
´´  
.
´´  !
GetDirectoryName
´´! 1
(
´´1 2
path
´´2 6
)
´´6 7
;
´´7 8
if
¨¨ 

(
¨¨ 
string
¨¨ 
.
¨¨ 
IsNullOrEmpty
¨¨  
(
¨¨  !
	directory
¨¨! *
)
¨¨* +
)
¨¨+ ,
{
≠≠ 	
return
ÆÆ 
path
ÆÆ 
;
ÆÆ 
}
ØØ 	
	Directory
±± 
.
±± 
CreateDirectory
±± !
(
±±! "
	directory
±±" +
)
±±+ ,
;
±±, -(
SetSecurePermissionsIfUnix
≤≤ "
(
≤≤" #
	directory
≤≤# ,
)
≤≤, -
;
≤≤- .
return
¥¥ 
path
¥¥ 
;
¥¥ 
}
µµ 
private
∑∑ 
static
∑∑ 

AppBuilder
∑∑ 
BuildAvaloniaApp
∑∑ .
(
∑∑. /
)
∑∑/ 0
=>
∑∑1 3

AppBuilder
∏∏ 
.
∏∏ 
	Configure
∏∏ 
<
∏∏ 
App
∏∏  
>
∏∏  !
(
∏∏! "
)
∏∏" #
.
∏∏# $
UsePlatformDetect
∏∏$ 5
(
∏∏5 6
)
∏∏6 7
.
∏∏7 8

LogToTrace
∏∏8 B
(
∏∏B C
)
∏∏C D
.
∏∏D E
UseReactiveUI
∏∏E R
(
∏∏R S
)
∏∏S T
;
∏∏T U
}ππ ⁄U
Ä/Users/oleksandrmelnychenko/RiderProjects/ecliptix-desktop/Ecliptix.Core/Ecliptix.Core.Desktop/Constants/ApplicationConstants.cs
	namespace 	
Ecliptix
 
. 
Core 
. 
Desktop 
.  
	Constants  )
;) *
public 
static 
class  
ApplicationConstants (
{ 
public 

static 
class 
ApplicationSettings +
{		 
public

 
const

 
string

 
APPLICATION_NAME

 ,
=

- .
$str

/ 9
;

9 :
public 
const 
string 
ENVIRONMENT_KEY +
=, -
$str. G
;G H
public 
const 
string #
DEVELOPMENT_ENVIRONMENT 3
=4 5
$str6 C
;C D
public 
const 
string "
PRODUCTION_ENVIRONMENT 2
=3 4
$str5 A
;A B
public 
const 
string #
DOT_NET_ENVIRONMENT_KEY 3
=4 5
$str6 J
;J K
public 
const 
string 
MUTEX_NAME_FORMAT -
=. /
$str0 E
;E F
} 
public 

static 
class 
Configuration %
{ 
public 
const 
string 
APP_SETTINGS_FILE -
=. /
$str0 B
;B C
public 
const 
string ,
 ENVIRONMENT_APP_SETTINGS_PATTERN <
== >
$str? U
;U V
public 
const 
string (
DEFAULT_APP_SETTINGS_SECTION 8
=9 :
$str; O
;O P
public 
const 
string (
SECURE_STORE_OPTIONS_SECTION 8
=9 :
$str; O
;O P
public 
const 
string "
SECURE_STORAGE_SECTION 2
=3 4
$str5 D
;D E
public 
const 
string 0
$SECRECY_CHANNEL_RETRY_POLICY_SECTION @
=A B
$strC ^
;^ _
public 
const 
string 
SERILOG_SECTION +
=, -
$str. 7
;7 8
public 
const 
string %
MINIMUM_LEVEL_DEFAULT_KEY 5
=6 7
$str8 N
;N O
public 
const 
string 
PATH_SEPARATOR *
=+ ,
$str- 0
;0 1
} 
public 

static 
class 
Storage 
{   
public!! 
const!! 
string!! %
DATA_PROTECTION_KEYS_PATH!! 5
=!!6 7
$str!!8 _
;!!_ `
public"" 
const"" 
string"" 
DEFAULT_STATE_PATH"" .
=""/ 0
$str""1 @
;""@ A
public## 
const## 
string## #
ECLIPTIX_DIRECTORY_NAME## 3
=##4 5
$str##6 @
;##@ A
public$$ 
const$$ 
string$$ !
LOCAL_SHARE_DIRECTORY$$ 1
=$$2 3
$str$$4 B
;$$B C
public%% 
const%% 
string%% )
APPLICATION_SUPPORT_DIRECTORY%% 9
=%%: ;
$str%%< Y
;%%Y Z
public&& 
const&& 
string&& 
LOGS_DIRECTORY&& *
=&&+ ,
$str&&- 3
;&&3 4
public'' 
const'' 
string'' 
LOG_FILE_PATTERN'' ,
=''- .
$str''/ >
;''> ?
public(( 
const(( 
string(( )
APP_DATA_ENVIRONMENT_VARIABLE(( 9
=((: ;
$str((< G
;((G H
})) 
public++ 

static++ 
class++ 
Timeouts++  
{,, 
public-- 
static-- 
readonly-- 
TimeSpan-- '
DefaultKeyLifetime--( :
=--; <
TimeSpan--= E
.--E F
FromDays--F N
(--N O
$num--O Q
)--Q R
;--R S
public.. 
static.. 
readonly.. 
TimeSpan.. '
HttpClientLifetime..( :
=..; <
TimeSpan..= E
...E F
FromMinutes..F Q
(..Q R
$num..R S
)..S T
;..T U
public// 
static// 
readonly// 
TimeSpan// '
HttpTimeout//( 3
=//4 5
TimeSpan//6 >
.//> ?
FromSeconds//? J
(//J K
$num//K L
)//L M
;//M N
public00 
static00 
readonly00 
TimeSpan00 '"
DefaultPollingInterval00( >
=00? @
TimeSpan00A I
.00I J
FromSeconds00J U
(00U V
$num00V X
)00X Y
;00Y Z
public11 
static11 
readonly11 
TimeSpan11 '$
DefaultInitialRetryDelay11( @
=11A B
TimeSpan11C K
.11K L
FromSeconds11L W
(11W X
$num11X Y
)11Y Z
;11Z [
public22 
static22 
readonly22 
TimeSpan22 ' 
DefaultMaxRetryDelay22( <
=22= >
TimeSpan22? G
.22G H
FromMinutes22H S
(22S T
$num22T U
)22U V
;22V W
}33 
public55 

static55 
class55 

Thresholds55 "
{66 
public77 
const77 
int77 %
DEFAULT_FAILURE_THRESHOLD77 2
=773 4
$num775 6
;776 7
public88 
const88 
int88 %
DEFAULT_SUCCESS_THRESHOLD88 2
=883 4
$num885 6
;886 7
public99 
const99 
int99 
DEFAULT_MAX_RETRIES99 ,
=99- .
$num99/ 1
;991 2
public:: 
const:: 
int:: 
RETRY_ATTEMPTS:: '
=::( )
$num::* +
;::+ ,
public;; 
const;; 
int;; $
EXPONENTIAL_BACKOFF_BASE;; 1
=;;2 3
$num;;4 5
;;;5 6
}<< 
public>> 

static>> 
class>> 
ConfigurationKeys>> )
{?? 
public@@ 
const@@ 
string@@ 
DEFAULT_THEME@@ )
=@@* +
$str@@, ;
;@@; <
publicAA 
constAA 
stringAA 
ENVIRONMENTAA '
=AA( )
$strAA* 7
;AA7 8
publicBB 
constBB 
stringBB )
DATA_CENTER_CONNECTION_STRINGBB 9
=BB: ;
$strBB< [
;BB[ \
publicCC 
constCC 
stringCC 
COUNTRY_CODE_APICC ,
=CC- .
$strCC/ A
;CCA B
publicDD 
constDD 
stringDD 
DOMAIN_NAMEDD '
=DD( )
$strDD* 7
;DD7 8
publicEE 
constEE 
stringEE 
CULTUREEE #
=EE$ %
$strEE& /
;EE/ 0
publicFF 
constFF 
stringFF  
ENCRYPTED_STATE_PATHFF 0
=FF1 2
$strFF3 I
;FFI J
publicGG 
constGG 
stringGG 

STATE_PATHGG &
=GG' (
$strGG) 5
;GG5 6
publicHH 
constHH 
stringHH 
INITIAL_RETRY_DELAYHH /
=HH0 1
$strHH2 G
;HHG H
publicII 
constII 
stringII 
MAX_RETRY_DELAYII +
=II, -
$strII. ?
;II? @
publicJJ 
constJJ 
stringJJ 
MAX_RETRIESJJ '
=JJ( )
$strJJ* 7
;JJ7 8
publicKK 
constKK 
stringKK 
PER_ATTEMPT_TIMEOUTKK /
=KK0 1
$strKK2 G
;KKG H
publicLL 
constLL 
stringLL 
USE_ADAPTIVE_RETRYLL .
=LL/ 0
$strLL1 E
;LLE F
}MM 
publicOO 

staticOO 
classOO 
LoggingOO 
{PP 
publicQQ 
constQQ 
stringQQ 
STARTUP_MESSAGEQQ +
=QQ, -
$strQQ. P
;QQP Q
publicRR 
constRR 
stringRR 
SHUTDOWN_MESSAGERR ,
=RR- .
$strRR/ J
;RRJ K
publicSS 
constSS 
stringSS 
FATAL_ERROR_MESSAGESS /
=SS0 1
$strSS2 q
;SSq r
publicTT 
constTT 
stringTT #
PERMISSIONS_SET_MESSAGETT 3
=TT4 5
$strTT6 h
;TTh i
publicUU 
constUU 
stringUU $
PERMISSIONS_FAIL_MESSAGEUU 4
=UU5 6
$strUU7 g
;UUg h
publicVV 
constVV 
stringVV '
GRPC_ENDPOINT_ERROR_MESSAGEVV 7
=VV8 9
$strVV: t
;VVt u
publicWW 
constWW 
stringWW $
PATH_EMPTY_ERROR_MESSAGEWW 4
=WW5 6
$strWW7 N
;WWN O
}XX 
publicZZ 

staticZZ 
classZZ 
FilePermissionsZZ '
{[[ 
public\\ 
const\\ 
UnixFileMode\\ !!
SECURE_DIRECTORY_MODE\\" 7
=\\8 9
UnixFileMode]] 
.]] 
UserRead]] !
|]]" #
UnixFileMode]]$ 0
.]]0 1
	UserWrite]]1 :
|]]; <
UnixFileMode]]= I
.]]I J
UserExecute]]J U
;]]U V
}^^ 
public`` 

static`` 
class`` 
	ExitCodes`` !
{aa 
publicbb 
constbb 
intbb 
FATAL_ERRORbb $
=bb% &
$numbb' (
;bb( )
}cc 
publicee 

staticee 
classee 
	LogLevelsee !
{ff 
publicgg 
constgg 
stringgg 
DEBUGgg !
=gg" #
$strgg$ +
;gg+ ,
publichh 
consthh 
stringhh 
INFORMATIONhh '
=hh( )
$strhh* 7
;hh7 8
publicii 
constii 
stringii 
WARNINGii #
=ii$ %
$strii& /
;ii/ 0
publicjj 
constjj 
stringjj 
ERRORjj !
=jj" #
$strjj$ +
;jj+ ,
publickk 
constkk 
stringkk 
FATALkk !
=kk" #
$strkk$ +
;kk+ ,
}ll 
}mm 