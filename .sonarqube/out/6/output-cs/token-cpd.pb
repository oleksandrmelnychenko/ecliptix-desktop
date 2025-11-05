€˙
i/Users/oleksandrmelnychenko/RiderProjects/ecliptix-desktop/Ecliptix.Core/Ecliptix.Core.Desktop/Program.cs
	namespaceKK 	
EcliptixKK
 
.KK 
CoreKK 
.KK 
DesktopKK 
;KK  
publicMM 
staticMM 
classMM 
ProgramMM 
{NN 
[OO 
	STAThreadOO 
]OO 
publicPP 

staticPP 
asyncPP 
TaskPP 
MainPP !
(PP! "
stringPP" (
[PP( )
]PP) *
argsPP+ /
)PP/ 0
{QQ 
stringRR 
	mutexNameRR 
=RR 
stringSS 
.SS 
FormatSS 
(SS  
ApplicationConstantsSS .
.SS. /
ApplicationSettingsSS/ B
.SSB C
MUTEX_NAME_FORMATSSC T
,SST U
EnvironmentSSV a
.SSa b
UserNameSSb j
)SSj k
;SSk l
usingTT 
MutexTT 
mutexTT 
=TT 
newTT 
(TT  
trueTT  $
,TT$ %
	mutexNameTT& /
,TT/ 0
outTT1 4
boolTT5 9

createdNewTT: D
)TTD E
;TTE F
ifVV 

(VV 
!VV 

createdNewVV 
)VV 
{WW 	
returnXX 
;XX 
}YY 	
IConfiguration[[ 
configuration[[ $
=[[% &
BuildConfiguration[[' 9
([[9 :
)[[: ;
;[[; <
Env\\ 
.\\ 
Load\\ 
(\\ 
)\\ 
;\\ 
Log]] 
.]] 
Logger]] 
=]] 
ConfigureSerilog]] %
(]]% &
configuration]]& 3
)]]3 4
;]]4 5
try__ 
{`` 	
Logaa 
.aa 
Informationaa 
(aa  
ApplicationConstantsaa 0
.aa0 1
Loggingaa1 8
.aa8 9
STARTUP_MESSAGEaa9 H
)aaH I
;aaI J
IServiceCollectionbb 
servicesbb '
=bb( )
ConfigureServicesbb* ;
(bb; <
configurationbb< I
)bbI J
;bbJ K
servicesdd 
.dd *
UseMicrosoftDependencyResolverdd 3
(dd3 4
)dd4 5
;dd5 6
IServiceProviderff 
serviceProviderff ,
=ff- .
servicesff/ 7
.ff7 8 
BuildServiceProviderff8 L
(ffL M
)ffM N
;ffN O

ReactiveUIgg 
.gg 
IViewLocatorgg #
reactiveViewLocatorgg$ 7
=gg8 9
serviceProvidergg: I
.ggI J
GetRequiredServiceggJ \
<gg\ ]

ReactiveUIgg] g
.ggg h
IViewLocatorggh t
>ggt u
(ggu v
)ggv w
;ggw x
Splathh 
.hh 
Locatorhh 
.hh 
CurrentMutablehh (
.hh( )
Registerhh) 1
(hh1 2
(hh2 3
)hh3 4
=>hh5 7
reactiveViewLocatorhh8 K
,hhK L
typeofhhM S
(hhS T

ReactiveUIhhT ^
.hh^ _
IViewLocatorhh_ k
)hhk l
)hhl m
;hhm n
BuildAvaloniaAppjj 
(jj 
)jj 
.jj +
StartWithClassicDesktopLifetimejj >
(jj> ?
argsjj? C
)jjC D
;jjD E
}kk 	
catchll 
(ll 
	Exceptionll 
exll 
)ll 
{mm 	
Lognn 
.nn 
Fatalnn 
(nn 
exnn 
,nn  
ApplicationConstantsnn .
.nn. /
Loggingnn/ 6
.nn6 7
FATAL_ERROR_MESSAGEnn7 J
)nnJ K
;nnK L
ifoo 
(oo 
configurationoo 
[oo  
ApplicationConstantsoo 2
.oo2 3
ApplicationSettingsoo3 F
.ooF G
ENVIRONMENT_KEYooG V
]ooV W
!=ooX Z 
ApplicationConstantspp $
.pp$ %
ApplicationSettingspp% 8
.pp8 9#
DEVELOPMENT_ENVIRONMENTpp9 P
)ppP Q
{qq 
Environmentrr 
.rr 
Exitrr  
(rr  ! 
ApplicationConstantsrr! 5
.rr5 6
	ExitCodesrr6 ?
.rr? @
FATAL_ERRORrr@ K
)rrK L
;rrL M
}ss 
}tt 	
finallyuu 
{vv 	
Logww 
.ww 
Informationww 
(ww  
ApplicationConstantsww 0
.ww0 1
Loggingww1 8
.ww8 9
SHUTDOWN_MESSAGEww9 I
)wwI J
;wwJ K
awaitxx 
Logxx 
.xx 
CloseAndFlushAsyncxx (
(xx( )
)xx) *
;xx* +
}yy 	
}zz 
private|| 
static|| 
IConfiguration|| !
BuildConfiguration||" 4
(||4 5
)||5 6
{}} 
string~~ 
?~~ 
environment~~ 
=~~ 
Env~~ !
.~~! "
	GetString~~" +
(~~+ , 
ApplicationConstants~~, @
.~~@ A
ApplicationSettings~~A T
.~~T U#
DOT_NET_ENVIRONMENT_KEY~~U l
)~~l m
;~~m n
environment
ÇÇ 
??=
ÇÇ "
ApplicationConstants
ÇÇ ,
.
ÇÇ, -!
ApplicationSettings
ÇÇ- @
.
ÇÇ@ A$
PRODUCTION_ENVIRONMENT
ÇÇA W
;
ÇÇW X
return
ÖÖ 
new
ÖÖ "
ConfigurationBuilder
ÖÖ '
(
ÖÖ' (
)
ÖÖ( )
.
ÜÜ 
SetBasePath
ÜÜ 
(
ÜÜ 

AppContext
ÜÜ #
.
ÜÜ# $
BaseDirectory
ÜÜ$ 1
)
ÜÜ1 2
.
áá 
AddJsonFile
áá 
(
áá "
ApplicationConstants
áá -
.
áá- .
Configuration
áá. ;
.
áá; <
APP_SETTINGS_FILE
áá< M
,
ááM N
optional
ááO W
:
ááW X
false
ááY ^
,
áá^ _
reloadOnChange
áá` n
:
áán o
true
ááp t
)
áát u
.
àà 
AddJsonFile
àà 
(
àà 
string
àà 
.
àà  
Format
àà  &
(
àà& '"
ApplicationConstants
àà' ;
.
àà; <
Configuration
àà< I
.
ààI J.
 ENVIRONMENT_APP_SETTINGS_PATTERN
ààJ j
,
ààj k
environment
ààl w
)
ààw x
,
ààx y
optional
ââ 
:
ââ 
true
ââ 
,
ââ 
reloadOnChange
ââ  .
:
ââ. /
true
ââ0 4
)
ââ4 5
.
ää %
AddEnvironmentVariables
ää $
(
ää$ %
)
ää% &
.
ãã 
Build
ãã 
(
ãã 
)
ãã 
;
ãã 
}
åå 
private
éé 
static
éé 
Logger
éé 
ConfigureSerilog
éé *
(
éé* +
IConfiguration
éé+ 9
configuration
éé: G
)
ééG H
{
èè 
try
êê 
{
ëë 	!
LoggerConfiguration
íí 
loggerConfig
íí  ,
=
íí- .
new
íí/ 2
(
íí2 3
)
íí3 4
;
íí4 5#
IConfigurationSection
îî !
serilogSection
îî" 0
=
îî1 2
configuration
ïï 
.
ïï 

GetSection
ïï (
(
ïï( )"
ApplicationConstants
ïï) =
.
ïï= >
Configuration
ïï> K
.
ïïK L
SERILOG_SECTION
ïïL [
)
ïï[ \
;
ïï\ ]
string
óó 
minLevel
óó 
=
óó 
serilogSection
óó ,
[
óó, -"
ApplicationConstants
óó- A
.
óóA B
Configuration
óóB O
.
óóO P'
MINIMUM_LEVEL_DEFAULT_KEY
óóP i
]
óói j
??
óók m"
ApplicationConstants
òò 2
.
òò2 3
	LogLevels
òò3 <
.
òò< =
INFORMATION
òò= H
;
òòH I
loggerConfig
ôô 
=
ôô 
minLevel
ôô #
switch
ôô$ *
{
öö "
ApplicationConstants
õõ $
.
õõ$ %
	LogLevels
õõ% .
.
õõ. /
DEBUG
õõ/ 4
=>
õõ5 7
loggerConfig
õõ8 D
.
õõD E
MinimumLevel
õõE Q
.
õõQ R
Debug
õõR W
(
õõW X
)
õõX Y
,
õõY Z"
ApplicationConstants
úú $
.
úú$ %
	LogLevels
úú% .
.
úú. /
INFORMATION
úú/ :
=>
úú; =
loggerConfig
úú> J
.
úúJ K
MinimumLevel
úúK W
.
úúW X
Information
úúX c
(
úúc d
)
úúd e
,
úúe f"
ApplicationConstants
ùù $
.
ùù$ %
	LogLevels
ùù% .
.
ùù. /
WARNING
ùù/ 6
=>
ùù7 9
loggerConfig
ùù: F
.
ùùF G
MinimumLevel
ùùG S
.
ùùS T
Warning
ùùT [
(
ùù[ \
)
ùù\ ]
,
ùù] ^"
ApplicationConstants
ûû $
.
ûû$ %
	LogLevels
ûû% .
.
ûû. /
ERROR
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
Error
ûûR W
(
ûûW X
)
ûûX Y
,
ûûY Z"
ApplicationConstants
üü $
.
üü$ %
	LogLevels
üü% .
.
üü. /
FATAL
üü/ 4
=>
üü5 7
loggerConfig
üü8 D
.
üüD E
MinimumLevel
üüE Q
.
üüQ R
Fatal
üüR W
(
üüW X
)
üüX Y
,
üüY Z
_
†† 
=>
†† 
loggerConfig
†† !
.
††! "
MinimumLevel
††" .
.
††. /
Information
††/ :
(
††: ;
)
††; <
}
°° 
;
°° 
loggerConfig
££ 
=
££ 
loggerConfig
££ '
.
££' (
WriteTo
££( /
.
££/ 0
Console
££0 7
(
££7 8
)
££8 9
;
££9 :
string
•• 
logPath
•• 
=
•• 
Path
•• !
.
••! "
Combine
••" )
(
••) *"
ApplicationConstants
••* >
.
••> ?
Storage
••? F
.
••F G
LOGS_DIRECTORY
••G U
,
••U V"
ApplicationConstants
¶¶ $
.
¶¶$ %
Storage
¶¶% ,
.
¶¶, -
LOG_FILE_PATTERN
¶¶- =
)
¶¶= >
;
¶¶> ?
loggerConfig
ßß 
=
ßß 
loggerConfig
ßß '
.
ßß' (
WriteTo
ßß( /
.
ßß/ 0
File
ßß0 4
(
ßß4 5
logPath
ßß5 <
,
ßß< =
rollingInterval
ßß> M
:
ßßM N
RollingInterval
ßßO ^
.
ßß^ _
Day
ßß_ b
)
ßßb c
;
ßßc d
return
©© 
loggerConfig
©© 
.
©©  
CreateLogger
©©  ,
(
©©, -
)
©©- .
;
©©. /
}
™™ 	
catch
´´ 
(
´´ 
	Exception
´´ 
)
´´ 
{
¨¨ 	
return
≠≠ 
new
≠≠ !
LoggerConfiguration
≠≠ *
(
≠≠* +
)
≠≠+ ,
.
ÆÆ 
MinimumLevel
ÆÆ 
.
ÆÆ 
Information
ÆÆ )
(
ÆÆ) *
)
ÆÆ* +
.
ØØ 
WriteTo
ØØ 
.
ØØ 
File
ØØ 
(
ØØ 
Path
∞∞ 
.
∞∞ 
Combine
∞∞  
(
∞∞  !"
ApplicationConstants
∞∞! 5
.
∞∞5 6
Storage
∞∞6 =
.
∞∞= >
LOGS_DIRECTORY
∞∞> L
,
∞∞L M"
ApplicationConstants
±± ,
.
±±, -
Storage
±±- 4
.
±±4 5
LOG_FILE_PATTERN
±±5 E
)
±±E F
,
±±F G
rollingInterval
±±H W
:
±±W X
RollingInterval
±±Y h
.
±±h i
Day
±±i l
)
±±l m
.
≤≤ 
CreateLogger
≤≤ 
(
≤≤ 
)
≤≤ 
;
≤≤  
}
≥≥ 	
}
¥¥ 
private
∂∂ 
static
∂∂  
IServiceCollection
∂∂ %
ConfigureServices
∂∂& 7
(
∂∂7 8
IConfiguration
∂∂8 F
configuration
∂∂G T
)
∂∂T U
{
∑∑ 
ServiceCollection
∏∏ 
services
∏∏ "
=
∏∏# $
new
∏∏% (
(
∏∏( )
)
∏∏) *
;
∏∏* +#
ConfigureCoreServices
∫∫ 
(
∫∫ 
services
∫∫ &
,
∫∫& '
configuration
∫∫( 5
)
∫∫5 6
;
∫∫6 7&
ConfigureNetworkServices
ªª  
(
ªª  !
services
ªª! )
)
ªª) *
;
ªª* +'
ConfigureSecurityServices
ºº !
(
ºº! "
services
ºº" *
,
ºº* +
configuration
ºº, 9
)
ºº9 :
;
ºº: ;(
ConfigureMessagingServices
ΩΩ "
(
ΩΩ" #
services
ΩΩ# +
)
ΩΩ+ ,
;
ΩΩ, --
ConfigureAuthenticationServices
ææ '
(
ææ' (
services
ææ( 0
)
ææ0 1
;
ææ1 2
ConfigureGrpc
øø 
(
øø 
services
øø 
)
øø 
;
øø  
ConfigureModules
¿¿ 
(
¿¿ 
services
¿¿ !
)
¿¿! "
;
¿¿" #
return
¬¬ 
services
¬¬ 
;
¬¬ 
}
√√ 
private
≈≈ 
static
≈≈ 
string
≈≈ 
GetSectionValue
≈≈ )
(
≈≈) *#
IConfigurationSection
≈≈* ?
section
≈≈@ G
,
≈≈G H
string
≈≈I O
key
≈≈P S
,
≈≈S T
string
≈≈U [
defaultValue
≈≈\ h
=
≈≈i j
$str
≈≈k m
)
≈≈m n
{
∆∆ 
return
«« 
section
«« 
[
«« 
key
«« 
]
«« 
??
«« 
defaultValue
«« +
;
««+ ,
}
»» 
private
   
static
   
void
   #
ConfigureCoreServices
   -
(
  - . 
IServiceCollection
  . @
services
  A I
,
  I J
IConfiguration
  K Y
configuration
  Z g
)
  g h
{
ÀÀ 
services
ÃÃ 
.
ÃÃ 

AddLogging
ÃÃ 
(
ÃÃ 
builder
ÃÃ #
=>
ÃÃ$ &
builder
ÃÃ' .
.
ÃÃ. /

AddSerilog
ÃÃ/ 9
(
ÃÃ9 :
dispose
ÃÃ: A
:
ÃÃA B
true
ÃÃC G
)
ÃÃG H
)
ÃÃH I
;
ÃÃI J
services
ŒŒ 
.
œœ 
AddDataProtection
œœ 
(
œœ 
)
œœ  
.
––  
SetApplicationName
–– 
(
––  "
ApplicationConstants
––  4
.
––4 5!
ApplicationSettings
––5 H
.
––H I
APPLICATION_NAME
––I Y
)
––Y Z
.
—— %
PersistKeysToFileSystem
—— $
(
——$ %
new
““ 
DirectoryInfo
““ !
(
““! "
ResolvePath
““" -
(
““- ."
ApplicationConstants
““. B
.
““B C
Storage
““C J
.
““J K'
DATA_PROTECTION_KEYS_PATH
““K d
)
““d e
)
““e f
)
”” 
.
‘‘ #
SetDefaultKeyLifetime
‘‘ "
(
‘‘" #"
ApplicationConstants
‘‘# 7
.
‘‘7 8
Timeouts
‘‘8 @
.
‘‘@ A 
DefaultKeyLifetime
‘‘A S
)
‘‘S T
;
‘‘T U
services
÷÷ 
.
÷÷ 
AddSingleton
÷÷ 
(
÷÷ 
configuration
÷÷ +
)
÷÷+ ,
;
÷÷, -
services
◊◊ 
.
◊◊ 
AddSingleton
◊◊ 
<
◊◊ 

IScheduler
◊◊ (
>
◊◊( )
(
◊◊) *
AvaloniaScheduler
◊◊* ;
.
◊◊; <
Instance
◊◊< D
)
◊◊D E
;
◊◊E F
}
ÿÿ 
private
⁄⁄ 
static
⁄⁄ 
void
⁄⁄ &
ConfigureNetworkServices
⁄⁄ 0
(
⁄⁄0 1 
IServiceCollection
⁄⁄1 C
services
⁄⁄D L
)
⁄⁄L M
{
€€ 
services
‹‹ 
.
‹‹ 
AddHttpClient
‹‹ 
(
‹‹ *
InternetConnectivityObserver
‹‹ ;
.
‹‹; <
HTTP_CLIENT_NAME
‹‹< L
,
‹‹L M
client
‹‹N T
=>
‹‹U W
{
›› 	1
#InternetConnectivityObserverOptions
ﬁﬁ /
options
ﬁﬁ0 7
=
ﬁﬁ8 91
#InternetConnectivityObserverOptions
ﬁﬁ: ]
.
ﬁﬁ] ^
Default
ﬁﬁ^ e
;
ﬁﬁe f
client
ﬂﬂ 
.
ﬂﬂ 
Timeout
ﬂﬂ 
=
ﬂﬂ 
options
ﬂﬂ $
.
ﬂﬂ$ %
ProbeTimeout
ﬂﬂ% 1
;
ﬂﬂ1 2
}
‡‡ 	
)
‡‡	 

;
‡‡
 
services
‚‚ 
.
‚‚ 
AddHttpClient
‚‚ 
<
‚‚ #
IIpGeolocationService
‚‚ 4
,
‚‚4 5"
IpGeolocationService
‚‚6 J
>
‚‚J K
(
‚‚K L
)
‚‚L M
.
„„  
SetHandlerLifetime
„„ 
(
„„  "
ApplicationConstants
„„  4
.
„„4 5
Timeouts
„„5 =
.
„„= > 
HttpClientLifetime
„„> P
)
„„P Q
.
‰‰ 
AddPolicyHandler
‰‰ 
(
‰‰ "
HttpPolicyExtensions
‰‰ 2
.
ÂÂ &
HandleTransientHttpError
ÂÂ )
(
ÂÂ) *
)
ÂÂ* +
.
ÊÊ 
OrResult
ÊÊ 
(
ÊÊ 
msg
ÊÊ 
=>
ÊÊ  
msg
ÊÊ! $
.
ÊÊ$ %

StatusCode
ÊÊ% /
==
ÊÊ0 2
HttpStatusCode
ÊÊ3 A
.
ÊÊA B
TooManyRequests
ÊÊB Q
)
ÊÊQ R
.
ÁÁ 
WaitAndRetryAsync
ÁÁ "
(
ÁÁ" #

retryCount
ËË 
:
ËË "
ApplicationConstants
ËË  4
.
ËË4 5

Thresholds
ËË5 ?
.
ËË? @
RETRY_ATTEMPTS
ËË@ N
,
ËËN O#
sleepDurationProvider
ÈÈ )
:
ÈÈ) *
attempt
ÈÈ+ 2
=>
ÈÈ3 5
TimeSpan
ÍÍ  
.
ÍÍ  !
FromSeconds
ÍÍ! ,
(
ÍÍ, -
Math
ÍÍ- 1
.
ÍÍ1 2
Pow
ÍÍ2 5
(
ÍÍ5 6"
ApplicationConstants
ÍÍ6 J
.
ÍÍJ K

Thresholds
ÍÍK U
.
ÍÍU V&
EXPONENTIAL_BACKOFF_BASE
ÍÍV n
,
ÍÍn o
attempt
ÎÎ #
)
ÎÎ# $
)
ÎÎ$ %
)
ÎÎ% &
)
ÎÎ& '
.
ÏÏ 
AddPolicyHandler
ÏÏ 
(
ÏÏ 
Policy
ÏÏ $
.
ÏÏ$ %
TimeoutAsync
ÏÏ% 1
<
ÏÏ1 2!
HttpResponseMessage
ÏÏ2 E
>
ÏÏE F
(
ÏÏF G"
ApplicationConstants
ÏÏG [
.
ÏÏ[ \
Timeouts
ÏÏ\ d
.
ÏÏd e
HttpTimeout
ÏÏe p
)
ÏÏp q
)
ÏÏq r
;
ÏÏr s
services
ÓÓ 
.
ÓÓ 
AddSingleton
ÓÓ 
<
ÓÓ +
IInternetConnectivityObserver
ÓÓ ;
,
ÓÓ; <*
InternetConnectivityObserver
ÓÓ= Y
>
ÓÓY Z
(
ÓÓZ [
)
ÓÓ[ \
;
ÓÓ\ ]
services
ÔÔ 
.
ÔÔ 
AddSingleton
ÔÔ 
(
ÔÔ 
new
ÔÔ !1
#InternetConnectivityObserverOptions
ÔÔ" E
{
 	
PollingInterval
ÒÒ 
=
ÒÒ "
ApplicationConstants
ÒÒ 2
.
ÒÒ2 3
Timeouts
ÒÒ3 ;
.
ÒÒ; <$
DefaultPollingInterval
ÒÒ< R
,
ÒÒR S
FailureThreshold
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
DEFAULT_FAILURE_THRESHOLD
ÚÚ? X
,
ÚÚX Y
SuccessThreshold
ÛÛ 
=
ÛÛ "
ApplicationConstants
ÛÛ 3
.
ÛÛ3 4

Thresholds
ÛÛ4 >
.
ÛÛ> ?'
DEFAULT_SUCCESS_THRESHOLD
ÛÛ? X
}
ÙÙ 	
)
ÙÙ	 

;
ÙÙ
 
services
ˆˆ 
.
ˆˆ 
AddSingleton
ˆˆ 
<
ˆˆ  
IRsaChunkEncryptor
ˆˆ 0
,
ˆˆ0 1
RsaChunkEncryptor
ˆˆ2 C
>
ˆˆC D
(
ˆˆD E
)
ˆˆE F
;
ˆˆF G
services
˜˜ 
.
˜˜ 
AddSingleton
˜˜ 
<
˜˜ $
IPendingRequestManager
˜˜ 4
,
˜˜4 5#
PendingRequestManager
˜˜6 K
>
˜˜K L
(
˜˜L M
)
˜˜M N
;
˜˜N O
services
˘˘ 
.
˘˘ 
AddSingleton
˘˘ 
<
˘˘ )
NetworkProviderDependencies
˘˘ 9
>
˘˘9 :
(
˘˘: ;
sp
˘˘; =
=>
˘˘> @
new
˘˘A D)
NetworkProviderDependencies
˘˘E `
(
˘˘` a
sp
˙˙ 
.
˙˙  
GetRequiredService
˙˙ !
<
˙˙! " 
IRpcServiceManager
˙˙" 4
>
˙˙4 5
(
˙˙5 6
)
˙˙6 7
,
˙˙7 8
sp
˚˚ 
.
˚˚  
GetRequiredService
˚˚ !
<
˚˚! "/
!IApplicationSecureStorageProvider
˚˚" C
>
˚˚C D
(
˚˚D E
)
˚˚E F
,
˚˚F G
sp
¸¸ 
.
¸¸  
GetRequiredService
¸¸ !
<
¸¸! ")
ISecureProtocolStateStorage
¸¸" =
>
¸¸= >
(
¸¸> ?
)
¸¸? @
,
¸¸@ A
sp
˝˝ 
.
˝˝  
GetRequiredService
˝˝ !
<
˝˝! ""
IRpcMetaDataProvider
˝˝" 6
>
˝˝6 7
(
˝˝7 8
)
˝˝8 9
)
˝˝9 :
)
˝˝: ;
;
˝˝; <
services
ˇˇ 
.
ˇˇ 
AddSingleton
ˇˇ 
<
ˇˇ %
NetworkProviderServices
ˇˇ 5
>
ˇˇ5 6
(
ˇˇ6 7
sp
ˇˇ7 9
=>
ˇˇ: <
new
ˇˇ= @%
NetworkProviderServices
ˇˇA X
(
ˇˇX Y
sp
ÄÄ 
.
ÄÄ  
GetRequiredService
ÄÄ !
<
ÄÄ! ""
IConnectivityService
ÄÄ" 6
>
ÄÄ6 7
(
ÄÄ7 8
)
ÄÄ8 9
,
ÄÄ9 :
sp
ÅÅ 
.
ÅÅ  
GetRequiredService
ÅÅ !
<
ÅÅ! "
IRetryStrategy
ÅÅ" 0
>
ÅÅ0 1
(
ÅÅ1 2
)
ÅÅ2 3
,
ÅÅ3 4
sp
ÇÇ 
.
ÇÇ  
GetRequiredService
ÇÇ !
<
ÇÇ! "$
IPendingRequestManager
ÇÇ" 8
>
ÇÇ8 9
(
ÇÇ9 :
)
ÇÇ: ;
)
ÇÇ; <
)
ÇÇ< =
;
ÇÇ= >
services
ÑÑ 
.
ÑÑ 
AddSingleton
ÑÑ 
<
ÑÑ %
NetworkProviderSecurity
ÑÑ 5
>
ÑÑ5 6
(
ÑÑ6 7
sp
ÑÑ7 9
=>
ÑÑ: <
new
ÑÑ= @%
NetworkProviderSecurity
ÑÑA X
(
ÑÑX Y
sp
ÖÖ 
.
ÖÖ  
GetRequiredService
ÖÖ !
<
ÖÖ! "/
!ICertificatePinningServiceFactory
ÖÖ" C
>
ÖÖC D
(
ÖÖD E
)
ÖÖE F
,
ÖÖF G
sp
ÜÜ 
.
ÜÜ  
GetRequiredService
ÜÜ !
<
ÜÜ! " 
IRsaChunkEncryptor
ÜÜ" 4
>
ÜÜ4 5
(
ÜÜ5 6
)
ÜÜ6 7
,
ÜÜ7 8
sp
áá 
.
áá  
GetRequiredService
áá !
<
áá! ""
IRetryPolicyProvider
áá" 6
>
áá6 7
(
áá7 8
)
áá8 9
)
áá9 :
)
áá: ;
;
áá; <
services
ââ 
.
ââ 
AddSingleton
ââ 
<
ââ 
NetworkProvider
ââ -
>
ââ- .
(
ââ. /
)
ââ/ 0
;
ââ0 1
services
ää 
.
ää 
AddSingleton
ää 
<
ää (
InternetConnectivityBridge
ää 8
>
ää8 9
(
ää9 :
)
ää: ;
;
ää; <
}
ãã 
private
çç 
static
çç 
void
çç '
ConfigureSecurityServices
çç 1
(
çç1 2 
IServiceCollection
çç2 D
services
ççE M
,
ççM N
IConfiguration
ççO ]
configuration
çç^ k
)
ççk l
{
éé 
services
èè 
.
èè 
AddSingleton
èè 
<
èè 
IOptions
èè &
<
èè& '#
DefaultSystemSettings
èè' <
>
èè< =
>
èè= >
(
èè> ?
_
èè? @
=>
èèA C
{
êê 	#
IConfigurationSection
ëë !
section
ëë" )
=
ëë* +
configuration
íí 
.
íí 

GetSection
íí (
(
íí( )"
ApplicationConstants
íí) =
.
íí= >
Configuration
íí> K
.
ííK L*
DEFAULT_APP_SETTINGS_SECTION
ííL h
)
ííh i
;
ííi j#
DefaultSystemSettings
ìì !
settings
ìì" *
=
ìì+ ,
new
ìì- 0
(
ìì0 1
)
ìì1 2
{
îî 
DefaultTheme
ïï 
=
ïï 
GetSectionValue
ïï .
(
ïï. /
section
ïï/ 6
,
ïï6 7"
ApplicationConstants
ïï8 L
.
ïïL M
ConfigurationKeys
ïïM ^
.
ïï^ _
DEFAULT_THEME
ïï_ l
)
ïïl m
,
ïïm n
Environment
ññ 
=
ññ 
GetSectionValue
ññ -
(
ññ- .
section
ññ. 5
,
ññ5 6"
ApplicationConstants
ññ7 K
.
ññK L
ConfigurationKeys
ññL ]
.
ññ] ^
ENVIRONMENT
ññ^ i
,
ññi j"
ApplicationConstants
óó (
.
óó( )!
ApplicationSettings
óó) <
.
óó< =$
PRODUCTION_ENVIRONMENT
óó= S
)
óóS T
,
óóT U(
DataCenterConnectionString
òò *
=
òò+ ,
GetSectionValue
òò- <
(
òò< =
section
òò= D
,
òòD E"
ApplicationConstants
ôô (
.
ôô( )
ConfigurationKeys
ôô) :
.
ôô: ;+
DATA_CENTER_CONNECTION_STRING
ôô; X
)
ôôX Y
,
ôôY Z
CountryCodeApi
öö 
=
öö  
GetSectionValue
öö! 0
(
öö0 1
section
öö1 8
,
öö8 9"
ApplicationConstants
öö: N
.
ööN O
ConfigurationKeys
ööO `
.
öö` a
COUNTRY_CODE_API
ööa q
)
ööq r
,
öör s

DomainName
õõ 
=
õõ 
GetSectionValue
õõ ,
(
õõ, -
section
õõ- 4
,
õõ4 5"
ApplicationConstants
õõ6 J
.
õõJ K
ConfigurationKeys
õõK \
.
õõ\ ]
DOMAIN_NAME
õõ] h
)
õõh i
,
õõi j
Culture
úú 
=
úú 
GetSectionValue
úú )
(
úú) *
section
úú* 1
,
úú1 2"
ApplicationConstants
úú3 G
.
úúG H
ConfigurationKeys
úúH Y
.
úúY Z
CULTURE
úúZ a
)
úúa b
,
úúb c
PrivacyPolicyUrl
ùù  
=
ùù! "
GetSectionValue
ùù# 2
(
ùù2 3
section
ùù3 :
,
ùù: ;
$str
ùù< N
)
ùùN O
,
ùùO P
TermsOfServiceUrl
ûû !
=
ûû" #
GetSectionValue
ûû$ 3
(
ûû3 4
section
ûû4 ;
,
ûû; <
$str
ûû= P
)
ûûP Q
,
ûûQ R

SupportUrl
üü 
=
üü 
GetSectionValue
üü ,
(
üü, -
section
üü- 4
,
üü4 5
$str
üü6 B
)
üüB C
}
†† 
;
†† 
return
°° 
Options
°° 
.
°° 
Create
°° !
(
°°! "
settings
°°" *
)
°°* +
;
°°+ ,
}
¢¢ 	
)
¢¢	 

;
¢¢
 
services
§§ 
.
§§ 
AddSingleton
§§ 
<
§§ 
IOptions
§§ &
<
§§& ' 
SecureStoreOptions
§§' 9
>
§§9 :
>
§§: ;
(
§§; <
_
§§< =
=>
§§> @
{
•• 	#
IConfigurationSection
¶¶ !
section
¶¶" )
=
¶¶* +
configuration
ßß 
.
ßß 

GetSection
ßß (
(
ßß( )"
ApplicationConstants
ßß) =
.
ßß= >
Configuration
ßß> K
.
ßßK L*
SECURE_STORE_OPTIONS_SECTION
ßßL h
)
ßßh i
;
ßßi j 
SecureStoreOptions
®® 
options
®® &
=
®®' (
new
®®) ,
(
®®, -
)
®®- .
{
©©  
EncryptedStatePath
™™ "
=
™™# $
ResolvePath
™™% 0
(
™™0 1
GetSectionValue
´´ #
(
´´# $
section
´´$ +
,
´´+ ,"
ApplicationConstants
´´- A
.
´´A B
ConfigurationKeys
´´B S
.
´´S T"
ENCRYPTED_STATE_PATH
´´T h
,
´´h i"
ApplicationConstants
¨¨ ,
.
¨¨, -
Storage
¨¨- 4
.
¨¨4 5 
DEFAULT_STATE_PATH
¨¨5 G
)
¨¨G H
)
≠≠ 
}
ÆÆ 
;
ÆÆ 
return
ØØ 
Options
ØØ 
.
ØØ 
Create
ØØ !
(
ØØ! "
options
ØØ" )
)
ØØ) *
;
ØØ* +
}
∞∞ 	
)
∞∞	 

;
∞∞
 
services
≤≤ 
.
≤≤ 
AddSingleton
≤≤ 
(
≤≤ 
sp
≤≤  
=>
≤≤! #
sp
≤≤$ &
.
≤≤& ' 
GetRequiredService
≤≤' 9
<
≤≤9 :
IOptions
≤≤: B
<
≤≤B C#
DefaultSystemSettings
≤≤C X
>
≤≤X Y
>
≤≤Y Z
(
≤≤Z [
)
≤≤[ \
.
≤≤\ ]
Value
≤≤] b
)
≤≤b c
;
≤≤c d
services
¥¥ 
.
¥¥ 
AddSingleton
¥¥ 
<
¥¥ 
ILogger
¥¥ %
<
¥¥% &.
 ApplicationSecureStorageProvider
¥¥& F
>
¥¥F G
>
¥¥G H
(
¥¥H I
sp
¥¥I K
=>
¥¥L N
sp
µµ 
.
µµ  
GetRequiredService
µµ !
<
µµ! "
ILoggerFactory
µµ" 0
>
µµ0 1
(
µµ1 2
)
µµ2 3
.
µµ3 4
CreateLogger
µµ4 @
<
µµ@ A.
 ApplicationSecureStorageProvider
µµA a
>
µµa b
(
µµb c
)
µµc d
)
∂∂ 	
;
∂∂	 

services
∑∑ 
.
∑∑ 
AddSingleton
∑∑ 
<
∑∑ /
!IApplicationSecureStorageProvider
∑∑ ?
,
∑∑? @.
 ApplicationSecureStorageProvider
∑∑A a
>
∑∑a b
(
∑∑b c
)
∑∑c d
;
∑∑d e
services
ππ 
.
ππ 
AddSingleton
ππ 
<
ππ '
IPlatformSecurityProvider
ππ 7
>
ππ7 8
(
ππ8 9
_
ππ9 :
=>
ππ; =
{
∫∫ 	
string
ªª 
appDataPath
ªª 
=
ªª  
Path
ªª! %
.
ªª% &
Combine
ªª& -
(
ªª- .
Environment
ºº 
.
ºº 
GetFolderPath
ºº )
(
ºº) *
Environment
ºº* 5
.
ºº5 6
SpecialFolder
ºº6 C
.
ººC D"
LocalApplicationData
ººD X
)
ººX Y
,
ººY Z"
ApplicationConstants
ΩΩ $
.
ΩΩ$ %
Storage
ΩΩ% ,
.
ΩΩ, -%
ECLIPTIX_DIRECTORY_NAME
ΩΩ- D
)
ΩΩD E
;
ΩΩE F
return
ææ 
new
ææ +
CrossPlatformSecurityProvider
ææ 4
(
ææ4 5
appDataPath
ææ5 @
)
ææ@ A
;
ææA B
}
øø 	
)
øø	 

;
øø
 
services
¡¡ 
.
¡¡ 
AddSingleton
¡¡ 
<
¡¡ )
ISecureProtocolStateStorage
¡¡ 9
>
¡¡9 :
(
¡¡: ;
sp
¡¡; =
=>
¡¡> @
{
¬¬ 	'
IPlatformSecurityProvider
√√ %
platformProvider
√√& 6
=
√√7 8
sp
√√9 ;
.
√√; < 
GetRequiredService
√√< N
<
√√N O'
IPlatformSecurityProvider
√√O h
>
√√h i
(
√√i j
)
√√j k
;
√√k l
IConfiguration
ƒƒ 
config
ƒƒ !
=
ƒƒ" #
sp
ƒƒ$ &
.
ƒƒ& ' 
GetRequiredService
ƒƒ' 9
<
ƒƒ9 :
IConfiguration
ƒƒ: H
>
ƒƒH I
(
ƒƒI J
)
ƒƒJ K
;
ƒƒK L
string
∆∆ 
storageDirectory
∆∆ #
=
∆∆$ %
config
«« 
[
«« "
ApplicationConstants
»» (
.
»»( )
Configuration
»») 6
.
»»6 7$
SECURE_STORAGE_SECTION
»»7 M
+
»»N O"
ApplicationConstants
…… (
.
……( )
Configuration
……) 6
.
……6 7
PATH_SEPARATOR
……7 E
+
……F G"
ApplicationConstants
……H \
.
……\ ]
ConfigurationKeys
……] n
.
……n o

STATE_PATH
……o y
]
……y z
??
   
Path
   
.
   
Combine
   
(
    
Environment
    +
.
  + ,
GetFolderPath
  , 9
(
  9 :
Environment
  : E
.
  E F
SpecialFolder
  F S
.
  S T"
LocalApplicationData
  T h
)
  h i
,
  i j"
ApplicationConstants
ÀÀ (
.
ÀÀ( )
Storage
ÀÀ) 0
.
ÀÀ0 1%
ECLIPTIX_DIRECTORY_NAME
ÀÀ1 H
)
ÀÀH I
;
ÀÀI J
byte
ÕÕ 
[
ÕÕ 
]
ÕÕ 
deviceId
ÕÕ 
=
ÕÕ 
Encoding
ÕÕ &
.
ÕÕ& '
UTF8
ÕÕ' +
.
ÕÕ+ ,
GetBytes
ÕÕ, 4
(
ÕÕ4 5
Environment
ÕÕ5 @
.
ÕÕ@ A
MachineName
ÕÕA L
+
ÕÕM N
Environment
ÕÕO Z
.
ÕÕZ [
UserName
ÕÕ[ c
)
ÕÕc d
;
ÕÕd e
return
œœ 
new
œœ (
SecureProtocolStateStorage
œœ 1
(
œœ1 2
platformProvider
œœ2 B
,
œœB C
storageDirectory
œœD T
,
œœT U
deviceId
œœV ^
)
œœ^ _
;
œœ_ `
}
–– 	
)
––	 

;
––
 
services
““ 
.
““ 
AddSingleton
““ 
<
““ /
!ICertificatePinningServiceFactory
““ ?
,
““? @.
 CertificatePinningServiceFactory
““A a
>
““a b
(
““b c
)
““c d
;
““d e
services
”” 
.
”” 
AddSingleton
”” 
<
”” &
IServerPublicKeyProvider
”” 6
,
””6 7%
ServerPublicKeyProvider
””8 O
>
””O P
(
””P Q
)
””Q R
;
””R S
}
‘‘ 
private
÷÷ 
static
÷÷ 
void
÷÷ (
ConfigureMessagingServices
÷÷ 2
(
÷÷2 3 
IServiceCollection
÷÷3 E
services
÷÷F N
)
÷÷N O
{
◊◊ 
services
ÿÿ 
.
ÿÿ 
AddSingleton
ÿÿ 
<
ÿÿ 
IMessageBus
ÿÿ )
,
ÿÿ) *

MessageBus
ÿÿ+ 5
>
ÿÿ5 6
(
ÿÿ6 7
)
ÿÿ7 8
;
ÿÿ8 9
services
ŸŸ 
.
ŸŸ 
AddSingleton
ŸŸ 
<
ŸŸ "
IConnectivityService
ŸŸ 2
,
ŸŸ2 3!
ConnectivityService
ŸŸ4 G
>
ŸŸG H
(
ŸŸH I
)
ŸŸI J
;
ŸŸJ K
services
⁄⁄ 
.
⁄⁄ 
AddSingleton
⁄⁄ 
<
⁄⁄ !
IBottomSheetService
⁄⁄ 1
,
⁄⁄1 2 
BottomSheetService
⁄⁄3 E
>
⁄⁄E F
(
⁄⁄F G
)
⁄⁄G H
;
⁄⁄H I
services
€€ 
.
€€ 
AddSingleton
€€ 
<
€€ '
ILanguageDetectionService
€€ 7
,
€€7 8&
LanguageDetectionService
€€9 Q
>
€€Q R
(
€€R S
)
€€S T
;
€€T U
services
‹‹ 
.
‹‹ 
AddSingleton
‹‹ 
<
‹‹ "
ILocalizationService
‹‹ 2
,
‹‹2 3!
LocalizationService
‹‹4 G
>
‹‹G H
(
‹‹H I
)
‹‹I J
;
‹‹J K
services
›› 
.
›› 
AddTransient
›› 
<
›› 
ILogoutService
›› ,
,
››, -
LogoutService
››. ;
>
››; <
(
››< =
)
››= >
;
››> ?
services
ﬂﬂ 
.
ﬂﬂ 
AddSingleton
ﬂﬂ 
<
ﬂﬂ &
IApplicationStateManager
ﬂﬂ 6
,
ﬂﬂ6 7%
ApplicationStateManager
ﬂﬂ8 O
>
ﬂﬂO P
(
ﬂﬂP Q
)
ﬂﬂQ R
;
ﬂﬂR S
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
ööw x
CultureInfo
õõ 
.
õõ 
InvariantCulture
õõ ,
,
õõ, -
out
õõ. 1
TimeSpan
õõ2 :
initialDelay
õõ; G
)
õõG H
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
ûûo p
CultureInfo
üü 
.
üü 
InvariantCulture
üü ,
,
üü, -
out
üü. 1
TimeSpan
üü2 :
maxDelay
üü; C
)
üüC D
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
••w x
CultureInfo
¶¶ 
.
¶¶ 
InvariantCulture
¶¶ ,
,
¶¶, -
out
¶¶. 1
TimeSpan
¶¶2 :
perAttemptTimeout
¶¶; L
)
¶¶L M
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
Environment
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
ºº (
DataCenterConnectionString
ºº 9
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