using System;
using System.Collections;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace pavlito
{
    internal class Program
    {
        private const byte LOGO_START = 0x1B; // байт перед логотипом
        private const byte FS = 0x1C; // разделитель данных
        private const byte STX = 0x02; // байт начала пакета
        private const byte ETX = 0x03; // байт окончания пакета

        private const byte ENQ = 0x05; // байт специальной команды для проверки связи
        private const byte ACK = 0x06; // байт ответа "ККТ на связи"

        private const string defaultPassword = "PIRI";

        private static byte[] testPacket = {
                0x02, // STX
                0x20, // packedID
                0x30, // commandCode
                0x30, // commandCode
                0x30, // errorCode
                0x30, // errorCode
                0x30, // data1 
                0x1C, // FS
                0x30, // data2
                0x1C, // FS
                0x30, // data3
                0x1C, // FS
                0x03, // ETX
                0x30, // CRC
                0x46  // CRC
            };
            
        private struct Packet
        {
            public string CommandCode;
            public string ErrorCode;
            public byte PacketId;
            public string[] Parameters;
        }

        private static Packet DecodePacket(byte[] packet)
        {
            byte shouldBeSTX = packet[0];

            byte packetId = packet[1];

            var commandCodeBytes = new byte[] { packet[2], packet[3] };
            var commandCodeString = Encoding.ASCII.GetString(commandCodeBytes);
            var commandCodeHex = BitConverter.ToString(commandCodeBytes);

            var errorCodeBytes = new byte[] { packet[4], packet[5] };
            var errorCodeString = Encoding.ASCII.GetString(errorCodeBytes);
            var errorCodeHex = BitConverter.ToString(errorCodeBytes);

            var headerLen = 0
                + 1 // STX
                + 1 // packetID
                + 2 // commandCode
                + 2 // errorCode
                + 1 // ETX
                + 2 // CRC
                ;

            var dataLen = packet.Length - headerLen;
            string[] data = null;
            if (dataLen > 0)
            {
                var dataArray = new byte[dataLen];
                Array.Copy(packet, 6, dataArray, 0, dataLen);
                var dataString = Encoding.GetEncoding(866).GetString(dataArray);
                var separators = new string[] { Encoding.GetEncoding(866).GetString(new byte[] { FS }) };
                data = dataString.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            }

            Packet result;
            result.CommandCode = commandCodeString;
            result.ErrorCode = errorCodeString;
            result.PacketId = packetId;
            result.Parameters = data;

            Console.WriteLine($"CommandCode={result.CommandCode}");
            Console.WriteLine($"ErrorCode={result.ErrorCode}");
            Console.WriteLine($"PacketId={result.PacketId}");

            var parametersCount = 0;
            if (result.Parameters != null)
            {
                parametersCount = result.Parameters.Count();
                foreach (var item in result.Parameters)
                {
                    Console.WriteLine($"Parameter: {item}");
                }
            }
            Console.WriteLine($"Parameters.Count={parametersCount}");
            return result;
        }

        private static byte[] CreatePacketByCode(string commandCodeHexString, string param1, string param2 = null)
        {
            var passwordString = defaultPassword;
            var passwordBytes = Encoding.ASCII.GetBytes(passwordString);

            byte packetId = 0x20;

            var commandCodeBytes = Encoding.ASCII.GetBytes(commandCodeHexString);


            byte[] paramBytes = null;
            byte[] param2Bytes = null;
            var param1len = 0;

            if (param1 != null)
            {
                paramBytes = Encoding.ASCII.GetBytes(param1);
                Array.Resize(ref paramBytes, paramBytes.Length + 1);
                paramBytes[paramBytes.Length - 1] = FS;
                param1len = paramBytes.Length;
            }

            if (param2 != null)
            {
                param2Bytes = Encoding.ASCII.GetBytes(param2);
                Array.Resize(ref paramBytes, paramBytes.Length + param2Bytes.Length + 1);
                Array.Copy(param2Bytes, 0, paramBytes, param1len, param2Bytes.Length);
                paramBytes[paramBytes.Length - 1] = FS;
            }


            var packetContentLen = 0
                + 4 // password
                + 1 // packetId
                + 2 // commandCode
                + (paramBytes is null ? 0 : paramBytes.Length) //param with FS
                + 1 // ETX
                ;

            var packetContent = new byte[packetContentLen];
            Array.Copy(passwordBytes, 0, packetContent, 0, 4); // INDEX 0-3 password
            packetContent[4] = packetId; // INDEX 4 packetId
            Array.Copy(commandCodeBytes, 0, packetContent, 5, 2); // INDEX 5-6 commandCode
            if (paramBytes != null)
            {
                Array.Copy(paramBytes, 0, packetContent, 7, paramBytes.Length);
            }
            packetContent[packetContent.Length - 1] = ETX;


            byte crc = 0;
            for (int i = 0; i < packetContent.Length; i++)
            {
                crc ^= packetContent[i];
            }
            var crcString = crc.ToString("X2");
            var crcBytes = Encoding.ASCII.GetBytes(crcString);

            var packetResult = new byte[packetContent.Length + 3];
            packetResult[0] = STX;
            Array.Copy(packetContent, 0, packetResult, 1, packetContent.Length);
            packetResult[packetResult.Length - 2] = crcBytes[0];
            packetResult[packetResult.Length - 1] = crcBytes[1];

            ConsolePrintPacket(packetResult);
            return packetResult;
        }

        private static void ConsolePrintPacket(byte[] packet)
        {
            var packetString = Encoding.ASCII.GetString(packet);
            var packetHexString = BitConverter.ToString(packet);

            Console.WriteLine("Подготовлен пакет данных для отправки:");
            Console.WriteLine(packetString);
            Console.WriteLine(packetHexString);
        }

        private static byte[] AwaitResponse(SerialPort port, int timeout = 1000)
        {
            Thread.Sleep(timeout);
            var bytesCount = port.BytesToRead;

            Console.WriteLine($"Байт в буфере порта: {bytesCount}");
            if (bytesCount == 0)
            {
                Console.WriteLine($"Ожидался ответ от ККМ, но ответ до таймаута в {timeout} мс не поступил.");
                return null;
            }

            var readedByteArray = new byte[bytesCount];

            port.Read(readedByteArray, 0, bytesCount);
            var readedHexString = BitConverter.ToString(readedByteArray);
            Console.WriteLine(readedHexString);
            return readedByteArray;
        }

        private static bool CheckConnectionKKT(SerialPort port)
        {
            byte[] command;
            Console.WriteLine(@"Отправка команды ""Проверка связи с ККТ"" (0x05)...");
            command = new byte[] { 0x05 };
            port.Write(command, 0, 1);
            Console.WriteLine("Команда проверки отправлена.");
            Console.WriteLine("Ожидание ответа от ККМ...");
            Thread.Sleep(1000);
            if (port.BytesToRead != 1) return false;
            var result = port.ReadByte();
            Console.WriteLine("Пришёл ответ: " + result);
            if (result != 6)
            {
                Console.WriteLine("Ответ некорректный.");
                return false;
            }
            Console.WriteLine("Ответ корректный.");
            return true;
        }

        private static byte[] StringToByteArray(String hex)
        {
            int NumberChars = hex.Length;
            byte[] bytes = new byte[NumberChars / 2];
            for (int i = 0; i < NumberChars; i += 2)
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            return bytes;
        }

        private static void WritePacketToPort(SerialPort port, byte[] packet)
        {
            port.Write(packet, 0, packet.Length);
            Console.WriteLine("Команда отправлена.");
            Console.WriteLine("Ожидание ответа от ККМ...");
        }

        public static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine($"Для вывода используется кодировка: {Console.OutputEncoding.EncodingName}");
            Console.WriteLine("Утилита выполняет прогрузку дизайна чека в автоматическом режиме на ККТ Пирит.");

            Console.WriteLine("Список доступных портов:");
            var ports = SerialPort.GetPortNames();
            foreach (var item in ports)
            {
                Console.WriteLine(item);
            }
            var portName = "/dev/ttyS0";

            var pathToComproxyINI = "/home/tc/storage/comproxy/ComProxy.ini";
            var physicalPortStr = "physical_port=";
            var lines = System.IO.File.ReadAllLines(pathToComproxyINI);
            foreach (var line in lines)
            {
                if (line.StartsWith(physicalPortStr, StringComparison.InvariantCultureIgnoreCase) == true)
                {
                    portName = line.Substring(physicalPortStr.Length);
                    Console.WriteLine($"Порт {portName} получен из {pathToComproxyINI}");
                }
            }

            var port = new SerialPort(portName, 57600, Parity.None, 8, StopBits.One);
            Console.WriteLine($"Попытка открыть порт {port.PortName}...");
            port.Open();
            Console.WriteLine($"Порт {port.PortName} открыт.");


            // Отправка специальной команды "Проверка соединения с ККТ"
            if (CheckConnectionKKT(port) == false)
            {
                Console.WriteLine("Ответ на запрос проверки соединения с ККТ не получен.");
                return;
            }


            // Команда "Запрос флагов статуса ККТ (0x00)"
            var packet00 = CreatePacketByCode("00", null);
            WritePacketToPort(port, packet00);

            var readedByteArray = AwaitResponse(port);
            Packet decoded;
            decoded = DecodePacket(readedByteArray);
            if (decoded.ErrorCode != "00") return;

            // Команда Запрос флагов статуса ККТ (0x00) возвращает три параметра. Во втором параметре бит с индексом 2 содержит признак открытой смены.
            var p2byte = Byte.Parse(decoded.Parameters[1]);
            var p2bits = new BitArray(new byte[] { p2byte });
            var IsShiftOpen = p2bits[2];
            Console.WriteLine($"Смена открыта={IsShiftOpen}");


            // Команда "Чтение таблицы настроек (0x11)"
            // с параметрами 30, 0 - 0 - (Строка[0..44]) Наименование организации, 1-ая строка
            var packet11 = CreatePacketByCode("11", "30", "0");
            WritePacketToPort(port, packet11);

            readedByteArray = AwaitResponse(port);
            decoded = DecodePacket(readedByteArray);
            if (decoded.ErrorCode != "00") return;

            var trimmed = decoded.Parameters[0].TrimStart();
            if (trimmed.Length < decoded.Parameters[0].Length)
            {
                Console.WriteLine("Наименование огранизации содержит центрующие пробелы.");
            }
            else
            {
                Console.WriteLine("Наименование организации НЕ содержит центрующие пробелы. Необходима ручная перерегистрация.");
            }


            // Команда "Загрузить дизайн чека (0x17)"
            var checkDesignBytes = System.IO.File.ReadAllBytes("new.DPirit_SD");
            var checkDesignFileSize = checkDesignBytes.Length;
            var packet17 = CreatePacketByCode("17", checkDesignFileSize.ToString());
            WritePacketToPort(port, packet17);

            var result = port.ReadByte();
            Console.WriteLine("Пришёл ответ: " + result);
            if (result != 6)
            {
                Console.WriteLine("Ответ некорректный.");
                return;
            }

            Console.WriteLine("Ответ корректный.");

            Console.WriteLine("Отправка дизайна чека...");
            port.Write(checkDesignBytes, 0, checkDesignFileSize);

            readedByteArray = AwaitResponse(port, 1000);
            decoded = DecodePacket(readedByteArray);

            if (decoded.ErrorCode != "00")
            {
                Console.WriteLine("Ошибка.");
                return;
            }
            Console.WriteLine("Прогрузка дизайна чека завершена успешно.");




            //Console.WriteLine("Прогрузка логотипа.");

            //var logoBmp = System.IO.File.ReadAllBytes("logo.bmp");
            //var logoBmpSize = logoBmp.Length;

            //var logoBytes = new byte[logoBmpSize + 1];
            //var logoBytesSize = logoBmpSize + 1;

            //logoBytes[0] = LOGO_START;
            //Array.Copy(logoBmp, 0, logoBytes, 1, logoBmpSize);

            //var packet15 = CreatePacketByCode("15", logoBytesSize.ToString());
            //ConsolePrintPacket(packet15);

            //Console.WriteLine("После нажатия Enter команда будет отправлена.");
            //Console.ReadLine();
            //port.Write(packet15, 0, packet15.Length);
            //Console.WriteLine("Команда отправлена.");

            //Console.WriteLine("Ожидание ответа от ККМ (первый этап загрузки логотипа)...");
            //result = port.ReadByte();
            //Console.WriteLine("Пришёл ответ: " + result);
            //if (result != 6)
            //{
            //    Console.WriteLine("Ответ некорректный.");
            //    return;
            //}

            //Console.WriteLine("Ответ корректный.");
            //Console.WriteLine("Отправка логотипа...");
            //port.Write(logoBytes, 0, logoBytesSize);

            //Thread.Sleep(1000);
            //bytesCount = port.BytesToRead;

            //Console.WriteLine($"Байт в буфере порта: {bytesCount}");
            //if (bytesCount == 0) return;

            //readedByteArray = new byte[bytesCount];
            //Console.WriteLine($"Размер буфера: {readedByteArray.Length}");

            //port.Read(readedByteArray, 0, bytesCount);
            //readedHexString = BitConverter.ToString(readedByteArray);
            //Console.WriteLine(readedHexString);

            //decoded = DecodePacket(readedByteArray);

            //Console.WriteLine($"CommandCode={decoded.CommandCode}");
            //Console.WriteLine($"ErrorCode={decoded.ErrorCode}");
            //Console.WriteLine($"PacketId={decoded.PacketId}");


            //Console.WriteLine("Прогрузка логотипа завершена успешно.");


            Console.WriteLine("Попытка закрыть порт.");
            port.Close();
            Console.WriteLine("Порт закрыт.");

        }
    }
}