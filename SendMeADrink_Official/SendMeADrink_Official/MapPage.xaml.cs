﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using System.Net.Http;
using SendMeADrink_Official.Database;
using SendMeADrink_Official.ProfileViews;
using SendMeADrink_Official.FinderViews;
using GoogleMaps = Xamarin.Forms.GoogleMaps;
using System.Reflection;
using Plugin.Geolocator;
using Geolocator = Plugin.Geolocator.Abstractions;
using Newtonsoft.Json;
using Xamarin.Essentials;

namespace SendMeADrink_Official
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class MapPage : MasterDetailPage
    {
        readonly App Current = (App)App.Current;
        public Geolocator.Position DBPosition; //Location used to get all places in a 5km radius from the database
        public Geolocator.Position LastPosition; //Last location of the user before the update

        public MapPage()
        {
            InitializeComponent();
            InitMapAndTracking();

            Current.FV = FinderView; //Store the grid in FV
            Current.FV.Children.Add(new MainView()); //Adding MainView to the FV grid (because it's the main view for the finder)
            UsernameLabel.BindingContext = Current.CU; //Binding the UsernameLabel with the CU variable
        }

        /*---------------------------------*/
        /*Map functions and event handeling*/
        public async void InitMapAndTracking()
        {
            //Get the current user location
            DBPosition = LastPosition = await GetUserStartLocation();

            //Get the style of the selected map
            var styleFile = await ChangeMapStyle();

            await Task.Run(() => 
            {
                Device.BeginInvokeOnMainThread(() =>
                {
                    //Changing the style of the map
                    CustomMap.MapStyle = GoogleMaps.MapStyle.FromJson(styleFile);

                    //Standard Map settings
                    CustomMap.UiSettings.ZoomControlsEnabled = false;
                    CustomMap.UiSettings.MyLocationButtonEnabled = true;

                    //move the users position on the map   
                    CustomMap.InitialCameraUpdate = GoogleMaps.CameraUpdateFactory.NewPositionZoom(new GoogleMaps.Position(DBPosition.Latitude, DBPosition.Longitude), 17.5);
                });
            });

            Current.CustomMap = CustomMap;

            //Store the long and lat data in the CU variable
            Current.CU.Latitude = DBPosition.Latitude;
            Current.CU.Longitude = DBPosition.Longitude;

            await GetPlaces(); //Calls the function to get all the places nearby
            await StartListening(); //Calls the function that will listen for location changes (every second)
        }

        /*Task that reads the JSON file and returns a string*/
        public static async Task<string> ChangeMapStyle()
        {
            string MapThemeString = null; //Variable to store the selected theme

            switch (Preferences.Get("AppThemeToken", 2))
            {
                case 0: MapThemeString = "Standard"; break;
                case 1: MapThemeString = "Retro"; break;
                case 2: MapThemeString = "Night"; break;
                default: break;
            }

            System.IO.StreamReader reader = null;

            await Task.Run(() => 
            {
                var assembly = typeof(MapPage).GetTypeInfo().Assembly;
                var stream = assembly.GetManifestResourceStream($"SendMeADrink_Official.MapRenders." + MapThemeString + ".json");
                reader = new System.IO.StreamReader(stream);
            });

            return reader.ReadToEnd();   
        }

        /*Task that gets the users location and returns it as a variable of type Position*/
        public static async Task<Geolocator.Position> GetUserStartLocation()
        {
            Geolocator.Position position = null; //Variable to store the users it's location data
            try
            {
                var locator = CrossGeolocator.Current;
                locator.DesiredAccuracy = 1;

                position = await locator.GetLastKnownLocationAsync(); //Check if a last known location is available

                if (position != null)
                {
                    //got a cahched position, so let's use it.
                    return position;
                }

                if (!locator.IsGeolocationAvailable || !locator.IsGeolocationEnabled)
                {
                    //not available or enabled
                    return null;
                }

                position = await locator.GetPositionAsync(TimeSpan.FromSeconds(1), null, true); //Getting the user it's current position
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unable to get location: " + ex);
            }

            if (position == null)
            {
                return null;
            }

            return position;
        }

        /*Move the user on the map*/
        public void UpdateUserLocation(Geolocator.Position position)
        {
            /*Checks if the variable "UpdateCamera" is set to true*/
            if(Current.UpdateCamera)
            {
                Task.Run(() =>
                {
                    Device.BeginInvokeOnMainThread(async() =>
                    {
                        /*Animate the camera the the user it's position with a zoom of 17.5 (zoom range: 2 - 21)*/
                        await CustomMap.AnimateCamera(GoogleMaps.CameraUpdateFactory.NewPositionZoom(new GoogleMaps.Position(position.Latitude, position.Longitude), 17.5)); 
                    });
                });
            }

            //Store the longitude and latitude data in the CU variable
            Current.CU.Latitude = position.Latitude;
            Current.CU.Longitude = position.Longitude;
        }

        /*Function to get all places nearby*/
        public async Task GetPlaces()
        {
            await Task.Run(async () =>
            {
                HttpClient client = new HttpClient(new HttpClientHandler());

                /*Creating a new variable of the type "FormUrlEncodedContent" to store the data that will be send to our database*/
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("Longitude", DBPosition.Longitude.ToString()),
                    new KeyValuePair<string, string>("Latitude", DBPosition.Latitude.ToString()),
                });

                HttpResponseMessage res = await client.PostAsync("http://send-meadrink.com/SMAD_App/Finder/GetPlaces.php", content); //send the variable content to the database as a POST method

                /*Checks if the data is retreived from the database*/
                if (res.IsSuccessStatusCode)
                {
                    var DBOutput = await res.Content.ReadAsStringAsync();
                    IList<Place> PlacesList = JsonConvert.DeserializeObject<IList<Place>>(DBOutput); //A list of places received from the database

                    Device.BeginInvokeOnMainThread(() =>
                    {
                        Current.Places = PlacesList; //Used to store all the places nearby (Variable is used when the page loads)
                        Current.PlacesListView.ItemsSource = PlacesList; //Used to change the list of places nearby when the location changes
                    });
                }
            });
        }

        //Used to convert degrees to radians
        public double ConvertToRadians(double angle)
        {
            return (Math.PI / 180) * angle;
        }

        /*Used to calculate the distance between the users current position and the place*/
        public async Task CalculateDistance(Geolocator.Position CurrentPosition)
        {
            await Task.Run(() => 
            { 
                //Used to re-calculate the distance for each place in the list
                foreach (Place place in Current.Places)
                {
                    place.Distance = 6371 * Math.Acos(Math.Cos(ConvertToRadians(CurrentPosition.Latitude)) * Math.Cos(ConvertToRadians(place.Latitude)) * Math.Cos(ConvertToRadians(place.Longitude) - ConvertToRadians(CurrentPosition.Longitude)) + Math.Sin(ConvertToRadians(CurrentPosition.Latitude)) * Math.Sin(ConvertToRadians(place.Latitude)));
                }
            });
        }

        /*Listens for location changes every second*/
        async Task StartListening()
        {
            if (CrossGeolocator.Current.IsListening)
            {
                return;
            }
            else
            {
                await CrossGeolocator.Current.StartListeningAsync(TimeSpan.FromSeconds(1), 1, true); //Start listening for location changes every second or every meter

                CrossGeolocator.Current.PositionChanged += PositionChanged;
                CrossGeolocator.Current.PositionError += PositionError;
            }
        }

        /*Handle the position change event*/
        private async void PositionChanged(object sender, Geolocator.PositionEventArgs e)
        {
            await Task.Run(async () => 
            {
                var CurrentPosition = e.Position; //Used to store all the users current positions data
                var Distance = 6371 * Math.Acos(Math.Cos(ConvertToRadians(CurrentPosition.Latitude)) * Math.Cos(ConvertToRadians(DBPosition.Latitude)) * Math.Cos(ConvertToRadians(DBPosition.Longitude) - ConvertToRadians(CurrentPosition.Longitude)) + Math.Sin(ConvertToRadians(CurrentPosition.Latitude)) * Math.Sin(ConvertToRadians(DBPosition.Latitude)));

                //If-statement to check if the distance between current user position and the position last time the database has been accessed is greater than or equal to 1 Km
                if (Distance >= 1)
                {
                    DBPosition = LastPosition = CurrentPosition;
                    await GetPlaces(); //Calls the function to get all the places nearby
                }
                else if ((CurrentPosition.Latitude != LastPosition.Latitude) || (CurrentPosition.Longitude != LastPosition.Longitude))
                {
                    await CalculateDistance(CurrentPosition); //Calls the function to re-calculate the distance between the user and each place in the list
                    LastPosition = CurrentPosition;
                }

                UpdateUserLocation(CurrentPosition); //Calls the function to update the user it's position on the map
            });
        }

        /*Handle the error message*/
        private void PositionError(object sender, Geolocator.PositionErrorEventArgs e)
        {
            Console.WriteLine(e.Error);
        }

        /*Handle the my location button clicked event*/
        private void MyLocationButton_Clicked(object sender, GoogleMaps.MyLocationButtonClickedEventArgs e)
        {
            Current.UpdateCamera = true;
        }

        /*---------------------------------*/
        /*Navigation menu*/
        private void Menu_Clicked(object sender, EventArgs e)
        {
            IsPresented = true; //This will present the MasterDetailPage.Master (Menu)
        }

        private void MapButton_Clicked(object sender, EventArgs e)
        {
            IsPresented = false; //This will hide the MasterDetailPage.Master (Menu)
        }

        /*Navigate to the profile page*/
        private async void ProfileButton_Clicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new Profile());
        }

        /*Navigate to the settings page*/
        private async void SettingsButton_Clicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new Settings());
        }

        private async void LogoutButton_Clicked(object sender, EventArgs e)
        {
            var res = await DisplayAlert("Would you like to logout", null, "Yes", "No"); //Alerts the user to logout

            /*Checks if the user would like to logout or not*/
            if (res == true)
            {
                Preferences.Remove("IsUserLoggedInToken");
                Application.Current.MainPage = new NavigationPage(new Login());
            }
        }
    }
}