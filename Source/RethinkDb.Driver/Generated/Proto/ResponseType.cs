


//AUTOGENERATED, DO NOTMODIFY.
//Do not edit this file directly.

#pragma warning disable 1591
// ReSharper disable CheckNamespace

namespace RethinkDb.Driver.Proto {
    public enum ResponseType {
        SUCCESS_ATOM = 1,
        SUCCESS_SEQUENCE = 2,
        SUCCESS_PARTIAL = 3,
        WAIT_COMPLETE = 4,
        SERVER_INFO = 5,
        CLIENT_ERROR = 16,
        COMPILE_ERROR = 17,
        RUNTIME_ERROR = 18,
    }

    
    
    public static class ResponseTypeLong{
        public const long SUCCESS_ATOM = 2462397222020719227; // {"t":1,"
        public const long SUCCESS_SEQUENCE = 2462398321532347003; // {"t":2,"
        public const long SUCCESS_PARTIAL =2462399421043974779; // {"t":3,"
        public const long WAIT_COMPLETE = 2462400520555602555; // {"t":4,"
        public const long SERVER_INFO = 2462401620067230331; // {"t":5,"
        public const long CLIENT_ERROR = 3185787912167105147; // {"t":16,
        public const long COMPILE_ERROR = 3186069387143815803; // {"t":17,
        public const long RUNTIME_ERROR = 3186350862120526459; // {"t":18,
        public const int Total = 8;
    }

       public static class ExtensionsForResponseType {
            public static bool IsError(this ResponseType rtype){
                 switch( rtype ){
                        case ResponseType.CLIENT_ERROR:
                            return true;
                        case ResponseType.COMPILE_ERROR:
                            return true;
                        case ResponseType.RUNTIME_ERROR:
                            return true;
                    default:
                        return false;
                 }
            }
      }


}
